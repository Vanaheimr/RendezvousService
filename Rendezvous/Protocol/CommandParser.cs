/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Vanaheimr RendezvousService <https://www.github.com/Vanaheimr/RendezvousService>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Diagnostics.CodeAnalysis;

#endregion

namespace org.GraphDefined.Vanaheimr.Rendezvous
{

    /// <summary>
    /// A hand-written recursive descent parser for the rendezvous control protocol.
    ///
    ///     command    = connect | disconnect
    ///     connect    = "ConnectPorts"    "(" portList "," secret [ "," profile ] ")"
    ///     disconnect = "DisconnectPorts" "(" ( portList | port { "," port } ) "," secret ")"
    ///     portList   = "[" portSpec { "," portSpec } "]"
    ///     portSpec   = "?" | 1..65535
    ///     secret     = 1..256 characters, no whitespace, no "()[],"
    ///     profile    = "Balanced" | "Interactive" | "Bulk" (and aliases)
    ///
    /// Command names and transfer profiles are case-insensitive, whitespace
    /// between all tokens is ignored.
    /// </summary>
    public static class CommandParser
    {

        #region Data

        /// <summary>
        /// The maximum length of a secret.
        /// </summary>
        public const Int32 MaxSecretLength     = 256;

        /// <summary>
        /// The minimum number of ports of a rendezvous.
        /// </summary>
        public const Int32 MinPortsPerSession  =   2;


        /// <summary>
        /// A single argument of a command: either a bracketed list of values,
        /// or a single value.
        /// </summary>
        private readonly record struct Argument(Boolean                IsList,
                                                IReadOnlyList<String>  Values,
                                                Int32                  Position);

        #endregion


        #region (static) TryParse(Text, out Command, out Error)

        /// <summary>
        /// Try to parse the given text as a rendezvous control command.
        /// </summary>
        /// <param name="Text">A text representation of a control command.</param>
        /// <param name="Command">The parsed control command.</param>
        /// <param name="Error">The parser error, when parsing failed.</param>
        public static Boolean TryParse(ReadOnlySpan<Char>                     Text,
                                       [NotNullWhen(true)]  out RendezvousCommand?  Command,
                                                            out CommandError        Error)
        {

            Command   = null;
            Error     = default;

            var position = 0;
            SkipWhitespace(Text, ref position);

            #region Command name

            var nameStart = position;

            while (position < Text.Length && IsLetter(Text[position]))
                position++;

            var name = Text[nameStart..position];

            if (name.IsEmpty)
            {
                Error = new (ResponseCode.UnknownCommand,
                             $"Expected a command name at position {nameStart}!");
                return false;
            }

            var isConnect = name.Equals(ConnectPortsCommand.   Name, StringComparison.OrdinalIgnoreCase);

            if (!isConnect &&
                !name.Equals(DisconnectPortsCommand.Name, StringComparison.OrdinalIgnoreCase))
            {
                Error = new (ResponseCode.UnknownCommand,
                             $"Unknown command '{name}', expected '{ConnectPortsCommand.Name}' or '{DisconnectPortsCommand.Name}'!");
                return false;
            }

            #endregion

            #region Arguments

            SkipWhitespace(Text, ref position);

            if (position >= Text.Length || Text[position] != '(')
            {
                Error = new (ResponseCode.InvalidSyntax,
                             $"Expected '(' at position {position}!");
                return false;
            }

            position++;

            if (!TryParseArguments(Text, ref position, out var arguments, out Error))
                return false;

            SkipWhitespace(Text, ref position);

            if (position != Text.Length)
            {
                Error = new (ResponseCode.InvalidSyntax,
                             $"Unexpected text after the end of the command at position {position}!");
                return false;
            }

            #endregion

            return isConnect
                       ? TryCreateConnectPorts   (arguments, out Command, out Error)
                       : TryCreateDisconnectPorts(arguments, out Command, out Error);

        }

        #endregion


        #region (private, static) TryParseArguments(Text, ref Position, out Arguments, out Error)

        /// <summary>
        /// Parse the comma separated arguments of a command, including the closing parenthesis.
        /// </summary>
        private static Boolean TryParseArguments(ReadOnlySpan<Char>     Text,
                                                 ref Int32              Position,
                                                 out List<Argument>     Arguments,
                                                 out CommandError       Error)
        {

            Arguments   = [];
            Error       = default;

            var afterComma = false;

            while (true)
            {

                SkipWhitespace(Text, ref Position);

                if (Position >= Text.Length)
                {
                    Error = new (ResponseCode.InvalidSyntax,
                                 $"Unexpected end of command, expected ')' at position {Position}!");
                    return false;
                }

                if (Text[Position] == ')')
                {

                    if (afterComma)
                    {
                        Error = new (ResponseCode.InvalidSyntax,
                                     $"Expected another argument after ',' at position {Position}!");
                        return false;
                    }

                    Position++;
                    return true;

                }

                var argumentPosition = Position;

                if (Text[Position] == '[')
                {

                    if (!TryParseList(Text, ref Position, out var values, out Error))
                        return false;

                    Arguments.Add(new (true, values, argumentPosition));

                }

                else
                {

                    if (!TryParseToken(Text, ref Position, out var token, out Error))
                        return false;

                    Arguments.Add(new (false, [token], argumentPosition));

                }

                SkipWhitespace(Text, ref Position);

                if (Position >= Text.Length)
                {
                    Error = new (ResponseCode.InvalidSyntax,
                                 $"Unexpected end of command, expected ',' or ')' at position {Position}!");
                    return false;
                }

                if (Text[Position] == ',')
                {
                    Position++;
                    afterComma = true;
                    continue;
                }

                if (Text[Position] == ')')
                {
                    Position++;
                    return true;
                }

                Error = new (ResponseCode.InvalidSyntax,
                             $"Expected ',' or ')' at position {Position}, but found '{Text[Position]}'!");
                return false;

            }

        }

        #endregion

        #region (private, static) TryParseList     (Text, ref Position, out Values, out Error)

        /// <summary>
        /// Parse a bracketed list of values, including both brackets.
        /// </summary>
        private static Boolean TryParseList(ReadOnlySpan<Char>  Text,
                                            ref Int32           Position,
                                            out List<String>    Values,
                                            out CommandError    Error)
        {

            Values   = [];
            Error    = default;

            Position++;   // '['

            var afterComma = false;

            while (true)
            {

                SkipWhitespace(Text, ref Position);

                if (Position >= Text.Length)
                {
                    Error = new (ResponseCode.InvalidSyntax,
                                 $"Unexpected end of command, expected ']' at position {Position}!");
                    return false;
                }

                if (Text[Position] == ']')
                {

                    if (afterComma)
                    {
                        Error = new (ResponseCode.InvalidSyntax,
                                     $"Expected another value after ',' at position {Position}!");
                        return false;
                    }

                    Position++;
                    return true;

                }

                if (!TryParseToken(Text, ref Position, out var token, out Error))
                    return false;

                Values.Add(token);

                SkipWhitespace(Text, ref Position);

                if (Position >= Text.Length)
                {
                    Error = new (ResponseCode.InvalidSyntax,
                                 $"Unexpected end of command, expected ',' or ']' at position {Position}!");
                    return false;
                }

                if (Text[Position] == ',')
                {
                    Position++;
                    afterComma = true;
                    continue;
                }

                if (Text[Position] == ']')
                {
                    Position++;
                    return true;
                }

                Error = new (ResponseCode.InvalidSyntax,
                             $"Expected ',' or ']' at position {Position}, but found '{Text[Position]}'!");
                return false;

            }

        }

        #endregion

        #region (private, static) TryParseToken    (Text, ref Position, out Token,  out Error)

        /// <summary>
        /// Parse a single value: a run of characters without whitespace and without delimiters.
        /// </summary>
        private static Boolean TryParseToken(ReadOnlySpan<Char>  Text,
                                             ref Int32           Position,
                                             out String          Token,
                                             out CommandError    Error)
        {

            Error = default;

            var start = Position;

            while (Position < Text.Length && IsTokenCharacter(Text[Position]))
                Position++;

            if (Position == start)
            {

                Token = String.Empty;
                Error = new (ResponseCode.InvalidSyntax,
                             $"Expected a value at position {start}, but found '{Text[start]}'!");

                return false;

            }

            Token = new String(Text[start..Position]);
            return true;

        }

        #endregion


        #region (private, static) TryCreateConnectPorts   (Arguments, out Command, out Error)

        private static Boolean TryCreateConnectPorts(List<Argument>                          Arguments,
                                                     [NotNullWhen(true)] out RendezvousCommand?  Command,
                                                                         out CommandError        Error)
        {

            Command = null;

            #region The ports must be given as a bracketed list

            if (Arguments.Count == 0 || !Arguments[0].IsList)
            {

                Error = new (ResponseCode.InvalidSyntax,
                             $"{ConnectPortsCommand.Name} expects a bracketed list of ports, e.g. {ConnectPortsCommand.Name}([20000, 30000], secret)!");

                return false;

            }

            var ports = new List<PortSpecification>(Arguments[0].Values.Count);

            foreach (var token in Arguments[0].Values)
            {

                if (!PortSpecification.TryParse(token, out var port))
                {

                    Error = new (ResponseCode.InvalidSyntax,
                                 $"Invalid TCP port '{token}', expected a number between 1 and 65535 or '?'!");

                    return false;

                }

                if (!port.IsRandom && ports.Contains(port))
                {

                    Error = new (ResponseCode.InvalidSyntax,
                                 $"Duplicate TCP port {port}!");

                    return false;

                }

                ports.Add(port);

            }

            if (ports.Count < MinPortsPerSession)
            {

                Error = new (ResponseCode.InvalidSyntax,
                             $"{ConnectPortsCommand.Name} requires at least {MinPortsPerSession} ports, but got {ports.Count}!");

                return false;

            }

            #endregion

            #region ... followed by the secret and an optional transfer profile

            if (Arguments.Count is < 2 or > 3)
            {

                Error = new (ResponseCode.InvalidSyntax,
                             $"{ConnectPortsCommand.Name} expects a list of ports, a secret and an optional transfer profile!");

                return false;

            }

            if (!TryGetSecret(Arguments[1], out var secret, out Error))
                return false;

            TransferProfile? profile = null;

            if (Arguments.Count == 3)
            {

                if (Arguments[2].IsList)
                {

                    Error = new (ResponseCode.InvalidSyntax,
                                 $"The transfer profile at position {Arguments[2].Position} must not be a list!");

                    return false;

                }

                if (!TransferProfileExtensions.TryParse(Arguments[2].Values[0], out var parsedProfile))
                {

                    Error = new (ResponseCode.InvalidSyntax,
                                 $"Unknown transfer profile '{Arguments[2].Values[0]}', expected 'Balanced', 'Interactive' or 'Bulk'!");

                    return false;

                }

                profile = parsedProfile;

            }

            #endregion

            Command = new ConnectPortsCommand(ports, secret, profile);
            return true;

        }

        #endregion

        #region (private, static) TryCreateDisconnectPorts(Arguments, out Command, out Error)

        private static Boolean TryCreateDisconnectPorts(List<Argument>                          Arguments,
                                                        [NotNullWhen(true)] out RendezvousCommand?  Command,
                                                                            out CommandError        Error)
        {

            Command = null;

            #region The ports may be given as a bracketed list, or as plain arguments

            IReadOnlyList<String> portTokens;
            Int32                 firstRemaining;

            if (Arguments.Count > 0 && Arguments[0].IsList)
            {
                portTokens      = Arguments[0].Values;
                firstRemaining  = 1;
            }

            else
            {

                var tokens = new List<String>();
                var index  = 0;

                // Consume every leading argument that looks like a fixed TCP port,
                // but always keep the last argument, as this is the secret.
                while (index < Arguments.Count - 1                              &&
                      !Arguments[index].IsList                                  &&
                       PortSpecification.TryParse(Arguments[index].Values[0], out var candidate) &&
                      !candidate.IsRandom)
                {
                    tokens.Add(Arguments[index].Values[0]);
                    index++;
                }

                portTokens      = tokens;
                firstRemaining  = index;

            }

            if (portTokens.Count == 0)
            {

                Error = new (ResponseCode.InvalidSyntax,
                             $"{DisconnectPortsCommand.Name} requires at least one TCP port, e.g. {DisconnectPortsCommand.Name}(20000, 30000, secret)!");

                return false;

            }

            var ports = new List<UInt16>(portTokens.Count);

            foreach (var token in portTokens)
            {

                if (!PortSpecification.TryParse(token, out var port) || port.IsRandom)
                {

                    Error = new (ResponseCode.InvalidSyntax,
                                 $"Invalid TCP port '{token}', expected a number between 1 and 65535!");

                    return false;

                }

                if (ports.Contains(port.Port!.Value))
                {

                    Error = new (ResponseCode.InvalidSyntax,
                                 $"Duplicate TCP port {port}!");

                    return false;

                }

                ports.Add(port.Port!.Value);

            }

            #endregion

            #region ... followed by the secret

            if (Arguments.Count - firstRemaining != 1)
            {

                Error = new (ResponseCode.InvalidSyntax,
                             $"{DisconnectPortsCommand.Name} expects one or more TCP ports followed by the secret!");

                return false;

            }

            if (!TryGetSecret(Arguments[firstRemaining], out var secret, out Error))
                return false;

            #endregion

            Command = new DisconnectPortsCommand(ports, secret);
            return true;

        }

        #endregion

        #region (private, static) TryGetSecret(Argument, out Secret, out Error)

        private static Boolean TryGetSecret(Argument          Argument,
                                            out String        Secret,
                                            out CommandError  Error)
        {

            Error   = default;
            Secret  = String.Empty;

            if (Argument.IsList)
            {

                Error = new (ResponseCode.InvalidSyntax,
                             $"The secret at position {Argument.Position} must not be a list!");

                return false;

            }

            var secret = Argument.Values[0];

            if (secret.Length > MaxSecretLength)
            {

                Error = new (ResponseCode.InvalidSyntax,
                             $"The secret must not be longer than {MaxSecretLength} characters!");

                return false;

            }

            Secret = secret;
            return true;

        }

        #endregion


        #region (private, static) SkipWhitespace(Text, ref Position)

        private static void SkipWhitespace(ReadOnlySpan<Char>  Text,
                                           ref Int32           Position)
        {
            while (Position < Text.Length && Char.IsWhiteSpace(Text[Position]))
                Position++;
        }

        #endregion

        #region (private, static) IsLetter(Character)

        private static Boolean IsLetter(Char Character)

            => Character is >= 'a' and <= 'z' ||
               Character is >= 'A' and <= 'Z';

        #endregion

        #region (private, static) IsTokenCharacter(Character)

        private static Boolean IsTokenCharacter(Char Character)

            => !Char.IsWhiteSpace(Character) &&
               Character is not ('(' or ')' or '[' or ']' or ',');

        #endregion

    }

}
