# Vanaheimr RendezvousService

A small TCP rendezvous service: a control client asks for two (or more) TCP ports,
the service opens one listener per port, waits for the clients and then relays the
data between them. Two clients get a plain bidirectional pipe, three or more clients
get a broadcast - a chat.

It comes in two parts:

| | |
|---|---|
| **`Rendezvous`** | The library. Everything needed to open, relay and close rendezvous, usable from any application. Its only dependency is `Microsoft.Extensions.Logging.Abstractions` - no hosting, no dependency injection, no configuration binder. |
| **`RendezvousService`** | The application: the TCP control endpoint, the generic host and the configuration binding. |

So the whole service is one way to use the library, not the only one - see
[The C# API](#the-c-api) below.

Everything is built on .NET 10 with `TimeProvider`, `System.Threading.Channels`
and pooled buffers.

## The control protocol

The control endpoint listens on `127.0.0.1:8500` by default and speaks a line based
UTF-8 protocol: one command per line (`LF` or `CR LF`), one response line per command
(always terminated by `CR LF`). A control connection may carry any number of commands.

### ConnectPorts

```
ConnectPorts([20000, 30000], secret)              open TCP/20000 and TCP/30000
ConnectPorts([?, 30000],     secret)              let the service pick the first port
ConnectPorts([20000, ?],     secret)              let the service pick the second port
ConnectPorts([?, ?],         secret)              let the service pick both ports
ConnectPorts([?, ?, ?],      secret)              three ports: a chat
ConnectPorts([?, ?],         secret, Interactive) with an explicit transfer profile
```

The response echoes the effective ports and transfer profile:

```
OK ConnectPorts([49312, 49313], Balanced)
```

### DisconnectPorts

Closes a rendezvous and removes its listeners. All given ports must belong to the
same rendezvous, one of them is enough to identify it. The secret must match the
secret of the `ConnectPorts` command.

```
DisconnectPorts(20000, 30000, secret)
DisconnectPorts([20000, 30000], secret)
DisconnectPorts(20000, secret)

OK DisconnectPorts([20000, 30000])
```

### Errors

```
ERROR <Code> <a human readable message>

ERROR InvalidSyntax    Invalid TCP port '70000', expected a number between 1 and 65535 or '?'!
ERROR PortInUse        TCP port 20000 is already used by another rendezvous!
ERROR PortNotAllowed   TCP port 20 is not within the allowed port range 1024 - 65535!
ERROR Unauthorized     The given secret does not authorize this command!
ERROR UnknownSession   There is no rendezvous on TCP port 20000!
ERROR TooManySessions  The service is already running 256 rendezvous!
ERROR TooManyPorts     A rendezvous must not have more than 16 ports!
ERROR CommandTooLong   A command must not be longer than 4096 bytes!
ERROR UnknownCommand   Unknown command 'Frobnicate', expected 'ConnectPorts' or 'DisconnectPorts'!
```

### The secret

Any non-empty string of up to 256 characters without whitespace and without
`( ) [ ] ,`. It is stored as a SHA-256 hash and compared in constant time, so
neither a memory dump nor the response time of the service reveal it.

The secret authorizes `DisconnectPorts` only - it is not a transport credential.
Anyone who can reach a rendezvous port can connect to it, so keep the control
endpoint on an interface you trust.

## The C# API

The text protocol is a thin layer over the `RendezvousManager`, so an application
embedding the library builds and executes the commands directly - no control
endpoint, no host, not even a configuration object:

```csharp
await using var manager = new RendezvousManager();     // that is all it takes

if (manager.TryConnectPorts(
        new ConnectPortsCommand(
            [PortSpecification.Random, PortSpecification.Fixed(30000)],
            "secret",
            TransferProfile.Interactive     // optional
        ),
        out var session,
        out var response))
{
    Console.WriteLine($"Waiting on TCP/[{String.Join(", ", session.Ports)}]");
    await session.Completion;               // ...until the rendezvous is closed
}
else
    Console.WriteLine(response.ToProtocolLine());
```

`TryConnectPorts` and `TryDisconnectPorts` hand out the `RendezvousSession` itself,
so the ports chosen for a `?` never have to be parsed out of the response text.
`ConnectPorts`/`DisconnectPorts` return the protocol response only, and
`Execute(String)` parses and executes a whole command line.

A rendezvous exposes `Ports`, `Profile`, `State`, `BytesRelayed`, `Completion` and
`Close(reason)`; `manager.TryGetSessionByPort(port, out var session)` finds an open
one. An unauthorized `TryDisconnectPorts` returns neither success nor the rendezvous.

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
request itself as well: at least two ports, no duplicates, no empty secret.

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

## Running

```bash
dotnet run --project RendezvousService
```

```bash
dotnet run --project RendezvousService -- --control-port 8500 --idle-timeout 00:30:00 --profile Bulk
```

Command line switches: `--control-address`, `--control-port`, `--data-address`,
`--min-port`, `--max-port`, `--rendezvous-timeout`, `--idle-timeout`, `--profile`.
Everything else is configured in `appsettings.json` (section `Rendezvous`) or via
`Rendezvous__*` environment variables.

A quick manual test with two terminals and `telnet`/`netcat`:

```bash
printf 'ConnectPorts([?,?], mySecret, chat)\r\n' | nc 127.0.0.1 8500
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
| `MaxCommandLength` | `4096` | Maximum length of a single command in bytes |
| `DefaultProfile` | `Balanced` | Transfer profile for commands without one |
| `Profiles.*` | see above | Buffer sizes and TCP parameters per profile |

An invalid configuration fails the startup with a list of all problems.

## Source layout

```
Rendezvous/                 the library, namespace org.GraphDefined.Vanaheimr.Rendezvous
  Configuration/            RendezvousOptions, TransferProfile, TransferProfileSettings
  Protocol/                 the command parser, commands and responses
  Sessions/                 RendezvousManager, RendezvousSession, SessionEndpoint, SessionJanitor
  Relay/                    PipeRelay (two clients), BroadcastRelay (chat), pooled chunks

RendezvousService/          the application, namespace ...Rendezvous.Service
  Control/                  ControlServer and ControlConnection
  Configuration/            RendezvousOptionsValidator (IValidateOptions)
  Program.cs                the generic host

RendezvousServiceTests/
```

Everything that binds to a framework - `BackgroundService`, `IOptions`,
`IValidateOptions`, the configuration binder - lives in the application. The
library sees none of it.

One type per file, named after that type. The only exception is
`TransferProfileExtensions`, which stays with the enum it parses and formats.

## Tests

```bash
dotnet test
```

142 NUnit tests: the command parser (including all of its error cases), the
configuration validation, the transfer profiles, real TCP relaying in both
directions, large transfers, half-close, chat broadcasting, port allocation and
rollback, authorization, the typed C# API, embedding the library without any
framework at all, and both timeouts - the latter driven by a `FakeTimeProvider`
instead of a sleeping test.

## License

Apache License 2.0, see [LICENSE](LICENSE).
