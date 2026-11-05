# Vanaheimr RendezvousService

A small TCP rendezvous service: a control client asks for two (or more) TCP ports,
the service opens one listener per port, waits for the clients and then relays the
data between them. Two clients get a plain bidirectional pipe, three or more clients
get a broadcast - a chat.

It comes in two parts:

| | |
|---|---|
| **`Hermod/Rendezvous`** | The library, within the [Hermod](https://github.com/Vanaheimr/Hermod) submodule: everything needed to open, relay and close rendezvous, *including* the TCP control endpoint. No hosting, no dependency injection, no configuration binder. |
| **`RendezvousService`** | This repository: the generic host, the configuration binding, and about ten lines tying the control endpoint into the host life cycle. |

So the whole service is one way to use the library, not the only one - see
[The C# API](#the-c-api) below.

Everything is built on .NET 10 with `TimeProvider`, `System.Threading.Channels`
and pooled buffers.

## The control protocol

The control endpoint listens on `127.0.0.1:8500` by default and speaks **signed
CBOR** ([RFC 8949](https://www.rfc-editor.org/rfc/rfc8949)). Its TCP port is open
to whoever can reach it, therefore a valid signature - not the network - is what
separates an operator from an attacker.

Every message, in both directions, is one length prefixed frame:

```
+------------+---------------------------------+
|   length   |      CBOR signed message        |
|  4 byte BE |          length bytes           |
+------------+---------------------------------+
```

CBOR is self-delimiting, but an explicit length lets the service reject an absurd
message before decoding a single byte of it.

### The envelope

```cddl
message = {
    1: bstr,          ; the payload, signed exactly as these bytes
    2: [+ signature]  ; one or more signatures
}

signature = {
    1: tstr,          ; the identification of the signing key
    2: int,           ; the COSE algorithm: -8 EdDSA, -48/-49/-50 ML-DSA-44/65/87
    3: bstr           ; the signature
}
```

The payload travels as a byte string, exactly as within COSE: the signature covers
the bytes that arrived, so no re-encoding of the parsed structure can ever change
what was verified. Payloads are written in the *deterministic* encoding of RFC 8949
§4.2.1, so an independent implementation can reproduce them byte for byte.

### The request payload

```cddl
request = {
    1: tstr,          ; "ConnectPorts" | "DisconnectPorts"
    2: [+ port],      ; port = uint / null       (null = '?', pick a free one)
  ? 3: tstr,          ; a description, e.g. "SSH rendezvous for maintenance work"
  ? 4: tstr,          ; the transfer profile
    5: bstr,          ; nonce, 8..64 bytes
    6: uint,          ; seconds since the Unix epoch
  ? 7: bool           ; echo everything back to its sender
}
```

The nonce and the timestamp are what make a captured request useless: the service
only accepts a timestamp within `MaxClockSkew` (5 minutes by default) and refuses
a nonce it has already seen within that window. A signature proves *who* sent a
request, never that it is fresh.

The description is plain metadata - up to 256 characters saying what a rendezvous
is for. It authorizes nothing; that is what the signatures are for.

### The response payload

```cddl
response = {
    1: int,           ; the response code, 0 = OK
    2: tstr,          ; a human readable message
  ? 3: [* uint],      ; the TCP ports of the rendezvous
  ? 4: tstr,          ; the effective transfer profile
  ? 5: bstr,          ; the nonce of the request this answers
    6: uint,          ; seconds since the Unix epoch
  ? 7: tstr,          ; the description of the rendezvous
  ? 8: uint,          ; when the rendezvous was opened
  ? 9: [* tstr],      ; the keys that opened the rendezvous
 ? 10: bool           ; whether the rendezvous echoes to the sender
}
```

Responses are signed as well, with the same envelope, so a client can tell a real
answer from an injected one. The nonce of the request is echoed, which binds a
response to exactly one request.

Keys 7 to 10 report back what the service recorded about a rendezvous: what it is
for, when it was opened, who owns it and whether it echoes.

Response codes: `OK`, `InvalidSyntax`, `Unauthorized`, `PortInUse`, `PortNotAllowed`,
`UnknownSession`, `TooManySessions`, `TooManyPorts`, `CommandTooLong`, `UnknownCommand`,
`InternalError`.

## Keys and signatures

Two signature families, and nothing else:

| | key | signature | |
|---|---|---|---|
| **Ed25519** | 32 B | 64 B | classical, ~128 bit |
| **Ed448** | 57 B | 114 B | classical, ~224 bit |
| **ML-DSA-44** | 1312 B | 2420 B | post-quantum, NIST level 2 |
| **ML-DSA-65** | 1952 B | 3309 B | post-quantum, NIST level 3 |
| **ML-DSA-87** | 2592 B | 4627 B | post-quantum, NIST level 5 |

EdDSA comes from BouncyCastle and is therefore always available; ML-DSA (FIPS 204)
is the native `System.Security.Cryptography.MLDsa` of .NET 10 and needs platform
support - check `ControlKey.IsMLDsaSupported`. Note the sizes: a single ML-DSA-87
signature is larger than the 4 KByte a line based protocol would ever have needed,
which is why `MaxFrameLength` defaults to 64 KByte.

Set `RequiredSignatures = 2` for the usual hybrid stance during the post-quantum
migration: one Ed25519 or Ed448 signature *and* one ML-DSA signature, so that a
break of either family alone authorizes nothing. Two signatures of the *same* key
still count as one - a key is trusted or it is not.

### Who may close a rendezvous

A signature answers *"may you talk to this service at all"*. It does not answer
*"is this rendezvous yours"* - and with several operators sharing one service,
those are different questions. So a rendezvous belongs to the keys that opened it:

* `ConnectPorts` records the identifications of the keys that signed the request.
* `DisconnectPorts` is only accepted from one of those keys.
* A key marked `IsAdministrator` may close every rendezvous - for the operator who
  has to clean up after a colleague who is not around. Give it to as few keys as
  possible.
* A caller *within the same process* holds the manager anyway and may do
  everything. `ControlAuthorization.TrustedAs("backup-job")` lets it name itself,
  so its rendezvous also record who opened them.

Revoking a key therefore also revokes its power over its own rendezvous, with
immediate effect - which a shared secret could never offer.

Every rendezvous records what a shared secret never could: `CreatedUtc` (when),
`CreatedBy` (which keys) and `Description` (what for). The same three go for every
control key: `Created`, `CreatedBy` and `Description`.

### Managing keys

Keys can be added and removed while the endpoint is serving requests; a compromised
key has to be revocable without a restart.

```csharp
// Generate an operator key and trust its public half
using var operatorKey = ControlSigner.GenerateEd25519("operator-1");

controlServer.Keys.Add(
    operatorKey.ToControlKey(
        NotBefore:    DateTimeOffset.UtcNow,
        NotAfter:     DateTimeOffset.UtcNow.AddDays(90),
        Description:  "The operator on duty",
        CreatedBy:    "achim"
    )
);

controlServer.Keys.Add(
    rootKey.ToControlKey(
        Description:      "Emergency clean-up",
        IsAdministrator:  true               // may close every rendezvous
    )
);

controlServer.Keys.AddMLDsa("operator-1-pq", mlDsaPublicKey, NotAfter: ...);

controlServer.Keys.Remove("operator-1");        // revoked, effective immediately
controlServer.Keys.RemoveExpired(DateTimeOffset.UtcNow);
```

`NotBefore` and `NotAfter` are checked against the arrival time of every single
request, not once at startup.

Within the application the same keys are configured in `appsettings.json` - public
keys only, a private key has no business being in a configuration file:

```json
"ControlKeys": [
  {
    "Id":              "operator-1",
    "KeyType":         "Ed25519",
    "PublicKey":       "<base64 of the raw public key>",
    "NotBefore":       "2026-01-01T00:00:00Z",
    "NotAfter":        "2026-12-31T23:59:59Z",
    "Description":     "The operator on duty",
    "IsAdministrator": false,
    "CreatedBy":       "achim"
  }
]
```

Without a single configured key the endpoint starts, says so loudly, and rejects
every command.

## The control client

`RendezvousControlClient` takes a remote socket, one or more private keys and a
port specification, sends the signed request and reports what came back:

```csharp
using var signer = ControlSigner.GenerateEd25519("operator-1");   // or load an existing key

var serverKeys = new ControlKeyRing();
serverKeys.Add(theServersResponseKey);            // so that responses are verified too

using var client = new RendezvousControlClient(
                       IPSocket.Parse("127.0.0.1:8500"),
                       signer,
                       serverKeys
                   );

var response = await client.ConnectPortsAsync(
                         [PortSpecification.Random, PortSpecification.Fixed(IPPort.Parse(30000))],
                         TransferProfile.Interactive,
                         "SSH rendezvous for maintenance work",
                         EchoToSender: false
                     );

if (response.IsSuccess)
    Console.WriteLine($"Rendezvous on TCP/[{String.Join(", ", response.Ports)}], " +
                      $"signed by {String.Join(", ", response.SignedBy.Select(key => key.Id))}, " +
                      $"owned by {String.Join(", ", response.CreatedBy)}");
else
    Console.WriteLine(response.Message);

await client.DisconnectPortsAsync([response.Ports[0]], "Maintenance is done");
```

Pass several signers to satisfy a `RequiredSignatures` of two or more. The result
tells a rejected command (`HasResponse == true`, `Code == Unauthorized`) apart from
a service that never answered (`HasResponse == false`, `Error` explains why) -
those are very different things for whoever has to react to them.

`DisconnectPortsAsync` only works on a rendezvous this client opened itself, unless
one of its keys is an administrator key.

## The C# API

The wire protocol is a thin layer over the `RendezvousManager`, so an application
embedding the library builds and executes the commands directly - no control
endpoint, no host, not even a configuration object:

```csharp
await using var manager = new RendezvousManager();     // that is all it takes

if (manager.TryConnectPorts(
        new ConnectPortsCommand(
            [PortSpecification.Random, PortSpecification.Fixed(IPPort.Parse(30000))],
            TransferProfile.Interactive,          // optional
            "SSH rendezvous for maintenance work" // optional
        ),
        out var session,
        out var response))
{
    Console.WriteLine($"Waiting on TCP/[{String.Join(", ", session.Ports)}]");
    await session.Completion;               // ...until the rendezvous is closed
}
else
    Console.WriteLine(response.Text);
```

`TryConnectPorts` and `TryDisconnectPorts` hand out the `RendezvousSession` itself,
so the ports chosen for a `?` never have to be parsed out of the response text.
`ConnectPorts`/`DisconnectPorts` return the protocol response only, and
`Execute(String)` parses and executes a whole command line:

```
ConnectPorts([20000, 30000])
ConnectPorts([?, 30000],  "SSH rendezvous for maintenance work")
ConnectPorts([?, ?],      Bulk)
ConnectPorts([?, ?, ?],   "The Friday chat", Interactive, Echo)
DisconnectPorts(20000, 30000)
DisconnectPorts([20000],  "Maintenance is done")
```

The arguments after the port list may come in any order, and each of them at most
once: an unquoted argument naming a transfer profile (`Balanced`, `Interactive`,
`Bulk` and their aliases) or the echo flag (`Echo`, `NoEcho`) is that, everything
else is the description. Quote it to say "this is a description".

A rendezvous exposes `Ports`, `Profile`, `State`, `BytesRelayed`, `CreatedUtc`,
`CreatedBy`, `Description`, `EchoToSender`, `Completion` and `Close(reason)`;
`manager.TryGetSessionByPort(port, out var session)` finds an open one. An
unauthorized `TryDisconnectPorts` returns neither success nor the rendezvous.

Every method takes an optional `ControlAuthorization` saying who is asking. Without
one the caller is trusted - it holds the manager anyway - and may open and close
everything:

```csharp
manager.TryConnectPorts(command, ControlAuthorization.TrustedAs("backup-job"), out var s, out var r);
manager.TryConnectPorts(command, new ControlAuthorization(theVerifiedKeys),    out var s, out var r);
```

The library speaks Hermod's own network types throughout: `IPPort` for TCP ports,
`IIPAddress` for addresses and `IPSocket` for the local and remote ends of a
connection. `IPPort` carries a `TypeConverter`, so it binds from `appsettings.json`
like any primitive.

The constructor takes an optional `RendezvousOptions`, `TimeProvider` and
`ILoggerFactory` - a plain object, not an `IOptions<T>`, so the library does not
drag the options framework into your application. An invalid configuration throws
right away instead of failing later on some port. Within a host it is registered
like this:

```csharp
builder.Services.AddSingleton(serviceProvider => new RendezvousManager(
                                                     serviceProvider.GetRequiredService<IOptions<RendezvousOptions>>().Value,
                                                     serviceProvider.GetRequiredService<TimeProvider>(),
                                                     serviceProvider.GetRequiredService<ILoggerFactory>()
                                                 ));
```

Note that the typed API bypasses the parser, therefore the manager validates the
request itself as well: at least two ports, no duplicates, no oversized description.

### The control endpoint without a host

The control endpoint is a plain object as well, so the complete service fits into
three lines - no host, no dependency injection:

```csharp
await using var manager  = new RendezvousManager(configuration);
await using var control  = new ControlServer(manager, configuration);

control.Start();                              // binds, so LocalEndPoint is known right away
...
await control.StopAsync();                    // stops accepting, waits for open connections
```

Within a generic host this is all the framework binding it takes - and note that
this is an `IHostedService`, not a `BackgroundService`: the base class would only
add bookkeeping for a loop that the control endpoint already manages itself.

```csharp
public sealed class ControlServerService(RendezvousManager Manager,
                                         IOptions<RendezvousOptions> Options,
                                         ILoggerFactory LoggerFactory) : IHostedService
{

    private readonly ControlServer controlServer = new (Manager, Options.Value, LoggerFactory);

    public Task StartAsync(CancellationToken token) { controlServer.Start(); return Task.CompletedTask; }
    public Task StopAsync (CancellationToken token) => controlServer.StopAsync().WaitAsync(token);

}
```

### Who enforces the timeouts?

The manager creates a `SessionJanitor` for itself and disposes it together with
the manager, so the timeouts work out of the box. The janitor is a plain object
with a `PeriodicTimer` - deliberately *not* an `IHostedService`, because a library
must not force a hosting framework onto its users.

Set `AutoMaintenance = false` to take over:

```csharp
await using var manager = new RendezvousManager(new RendezvousOptions { AutoMaintenance = false });

manager.Sweep();                    // whenever it suits you, e.g. from your own scheduler

// ...or run the janitor yourself, on your own clock:
await using var janitor = new SessionJanitor(manager, TimeSpan.FromSeconds(15), timeProvider);
janitor.Start();
```

## Transfer profiles

The optional third argument of `ConnectPorts` tells the service what the connection
will be used for. It sizes the relay buffers and tunes the TCP parameters of the
accepted sockets:

| | `Interactive` | `Balanced` (default) | `Bulk` |
|---|---|---|---|
| aliases | `low-latency`, `chat`, `ssh`, `terminal` | `default`, `normal`, `auto` | `throughput`, `transfer`, `backup` |
| for | chat, SSH, remote control | unknown traffic | file transfers, backups |
| relay buffer | 8 KByte | 64 KByte | 256 KByte |
| TCP_NODELAY | yes | yes | no |
| SO_RCVBUF / SO_SNDBUF | 32 KByte | operating system | operating system |
| first keep-alive probe | after 30 sec | after 60 sec | after 120 sec |
| chat queue per client | 512 chunks / 1 MByte | 128 chunks / 4 MByte | 32 chunks / 16 MByte |

Small relay buffers add less latency per read/write pair, large relay buffers need
fewer system calls per transferred megabyte. The socket buffers are left to the
operating system for `Balanced` and `Bulk` on purpose: an explicit `SO_RCVBUF`
disables the receive window auto-tuning, which is exactly what a bulk transfer over
a long fat network needs most.

Every value of every profile can be overridden in `appsettings.json`.

## Life cycle and timeouts

```
Pending  ---- all clients arrived ---->  Established  ---- all clients gone ---->  Closed
   |                                          |                                      ^
   +---- rendezvous timeout (5 min) ----------+---- idle timeout (10 min) -----------+
```

* **Rendezvous timeout** (default 5 minutes): when not all clients arrive, the
  rendezvous is closed and its TCP ports are released.
* **Idle timeout** (default 10 minutes): an established rendezvous without any
  relayed payload is closed. TCP keep-alive probes are a transport level
  mechanism and do not count as payload - they detect half-open connections
  (a peer that lost power) much earlier than any application timeout could.
* A rendezvous is also closed as soon as its clients are gone, or on an
  authorized `DisconnectPorts`.

Details worth knowing:

* Each listener serves exactly one client. The listener nevertheless stays open
  until the rendezvous is closed, so that nobody else can take over the port in
  the meantime. Additional connections on the same port are rejected immediately.
* Relaying starts when all clients have arrived. Data sent by an early client is
  not lost, it simply stays in the TCP receive buffer until the rendezvous is
  established.
* For two clients a half-close is forwarded: when one side shuts down its sending
  direction, the other side sees the end of the stream but may still answer. This
  is what SSH port forwarding and `cat file | nc ...` expect.
* For three or more clients everything one client sends is forwarded to all others,
  but never back to the sender. The service does not add any framing - the chat
  protocol itself is up to the clients. Every client has its own bounded outbound
  queue, so one slow reader can not stall the whole chat; a client that exceeds
  its queue limits is disconnected.

### The echo to the sender

A sender never learns where its own bytes ended up within the conversation: the
service decides how several senders interleave, and everybody else sees that
merged stream - only the sender does not. `EchoToSender` closes that gap by
sending everything back to its sender as well:

```csharp
new ConnectPortsCommand([...], TransferProfile.Interactive, "The Friday chat", EchoToSender: true)
```

```
ConnectPorts([?,?,?], "The Friday chat", Interactive, Echo)
```

Then **every** client - the sender included - receives the very same byte stream,
and the service is the one and only sequencer of the conversation. The fan-out of
one chunk to all queues happens under a lock, so that a chunk reaches every queue
before the next one reaches any of them; without that, two clients sending at the
same time could reach the queues in a different order and every receiver would end
up with its own version of the conversation.

It is off by default, and two things are worth knowing before turning it on:

* **A client that only writes and never reads is disconnected.** Everything it
  sends comes back, fills its own outbound queue and eventually exceeds it. Without
  the echo this can not happen, so a write-only client is fine there.
* **Two clients with an echo lose the half-close propagation**, because they are
  then relayed by the broadcast rather than by the pipe. That is the right trade:
  whoever asks for an echo is having a conversation, not tunneling a protocol.

## Running

```bash
dotnet run --project RendezvousService
```

```bash
dotnet run --project RendezvousService -- --control-port 8500 --idle-timeout 00:30:00 --profile Bulk
```

Command line switches: `--control-address`, `--control-port`, `--data-address`,
`--min-port`, `--max-port`, `--rendezvous-timeout`, `--idle-timeout`, `--profile`.
Everything else is configured in `appsettings.json` (section `Rendezvous`), via
`Rendezvous__*` environment variables, or in its long form on the command line,
e.g. `--Rendezvous:MaxSessions 42`.

`appsettings.json` is read from next to the binary, whatever the working
directory: a systemd unit needs no `WorkingDirectory` for it, and `dotnet run
--project RendezvousService` from the repository reads it as well.

The control endpoint speaks signed CBOR, so it can not be driven from a shell -
open the rendezvous with `RendezvousControlClient` (see [The control
client](#the-control-client)). The *data* ports are plain TCP, so `telnet` and
`netcat` are all it takes to meet on them:

```bash
nc 127.0.0.1 20000
```

## Configuration

| Setting | Default | Description |
|---|---|---|
| `ControlAddress` | `127.0.0.1` | Interface of the control endpoint |
| `ControlPort` | `8500` | TCP port of the control endpoint, `0` picks a free port |
| `DataAddress` | `0.0.0.0` | Interface of the rendezvous listeners |
| `MinDataPort` / `MaxDataPort` | `1024` / `65535` | Allowed TCP ports for a rendezvous |
| `RendezvousTimeout` | `00:05:00` | Maximum time until all clients arrived |
| `IdleTimeout` | `00:10:00` | Maximum time without relayed payload |
| `MaintenanceInterval` | `00:00:15` | How often timed out rendezvous are looked for |
| `AutoMaintenance` | `true` | Whether the manager looks after the timeouts itself |
| `ControlIdleTimeout` | `00:02:00` | Maximum time a control connection may be silent |
| `MaxSessions` | `256` | Maximum number of concurrent rendezvous |
| `MaxPortsPerSession` | `16` | Maximum number of clients per rendezvous |
| `MaxControlConnections` | `64` | Maximum number of concurrent control connections |
| `MaxFrameLength` | `65536` | Maximum length of one control message in bytes |
| `RequiredSignatures` | `1` | How many distinct valid signatures a request needs |
| `MaxClockSkew` | `00:05:00` | Accepted age of a request, and how long a nonce is remembered |
| `ControlKeys` | empty | The public keys that may authorize commands |
| `DefaultProfile` | `Balanced` | Transfer profile for commands without one |
| `Profiles.*` | see above | Buffer sizes and TCP parameters per profile |

An invalid configuration fails the startup with a list of all problems.

## Source layout

```
libs/Hermod/Hermod/Rendezvous/     the library (submodule)
  Configuration/                   RendezvousOptions, TransferProfile, TransferProfileSettings
  Protocol/                        the command parser, commands and responses
  Sessions/                        RendezvousManager, RendezvousSession, SessionEndpoint, SessionJanitor
  Relay/                           PipeRelay (two clients), BroadcastRelay (chat), pooled chunks
  Security/                        ControlKey, ControlKeyRing, ControlSigner, ControlAuthorization, NonceCache
  Control/                         ControlServer, ControlConnection, RendezvousControlClient

libs/Hermod/HermodTests/Rendezvous/   its tests (submodule)

RendezvousService/                 the application, namespace ...Rendezvous.Service
  Hosting/                         ControlServerService (IHostedService, ten lines),
                                   RendezvousServiceCollectionExtensions (AddRendezvousService)
  Configuration/                   RendezvousOptionsValidator (IValidateOptions),
                                   RendezvousCommandLine (the command line switches)
  Program.cs                       the generic host, built from the two above

RendezvousServiceTests/            tests for the application, none for the library
```

`Program.cs` holds no logic of its own: the command line switches and every
registration live in `RendezvousCommandLine` and `AddRendezvousService`, so that
the tests compose and start the very same host.

Everything that binds to a framework - `IHostedService`, `IOptions`,
`IValidateOptions`, the configuration binder - lives in the application. The
library sees none of it, and every one of its long running parts offers a plain
`Start()` / `StopAsync()` pair instead.

One type per file, named after that type. The only exception is
`TransferProfileExtensions`, which stays with the enum it parses and formats.

## Tests

The tests live where the code they cover lives:

```bash
dotnet test libs/Hermod/HermodTests/HermodTests.csproj --filter "FullyQualifiedName~Hermod.Tests.Rendezvous"
dotnet test RendezvousServiceTests/RendezvousServiceTests.csproj
```

189 NUnit tests for the library, 20 for the application: the hosted service, the
options validator, the configuration binding, the command line switches, the
host composed by `AddRendezvousService` - started, stopped, refusing an invalid
configuration, and handing the configuration on to the `RendezvousManager` - and
one that starts the service as a process from a foreign working directory, to
prove it still finds its `appsettings.json`.

The library tests cover the command parser (including all of its error cases), the
configuration validation, the transfer profiles, real TCP relaying in both
directions, large transfers, half-close, chat broadcasting, port allocation and
rollback, authorization, the typed C# API, embedding the library without any
framework at all, and both timeouts - the latter driven by Hermod's
`FakeTimeProvider` instead of a sleeping test. Only the three tests asking
whether the janitor loop really runs use the real clock, with timeouts in
milliseconds, because a faked timer could not answer that question.

One of them is a genuine concurrency test: five clients, four of them talking at
the same time with the echo turned on, and every one of the five has to end up
with a byte-identical stream. Without the lock around the fan-out it fails
reliably, and it names the reason - "the service is not the only sequencer".

The signed CBOR protocol has tests of its own, and they are written as attacks:
an unknown key, a tampered payload behind a valid signature, an expired key, a key
that is not valid yet, a key revoked mid-session, a replayed request, a stale one,
one from the future, two signatures of the same key where two distinct ones are
required, and a client that pins the wrong response key. Each of them must be
rejected - and Ed25519, Ed448 and all three ML-DSA parameter sets must be accepted.

## License

Apache License 2.0, see [LICENSE](LICENSE).
