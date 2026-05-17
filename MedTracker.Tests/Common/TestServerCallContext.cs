using Grpc.Core;

namespace MedTracker.Tests.Common;

/// <summary>
/// Минимальный fake ServerCallContext для unit-тестов интерцепторов.
/// ServerCallContext — abstract, поэтому делаем простой подкласс с no-op overrides.
/// </summary>
public class FakeServerCallContext : ServerCallContext
{
    public static FakeServerCallContext Create(string method, string peer = "ipv4:127.0.0.1:54321")
        => new(method, peer);

    private readonly string _method;
    private readonly string _peer;
    private readonly Metadata _requestHeaders = new();
    private readonly Metadata _responseTrailers = new();
    private readonly AuthContext _authContext = new("", new Dictionary<string, List<AuthProperty>>());
    private readonly Dictionary<object, object> _userState = new();

    private FakeServerCallContext(string method, string peer)
    {
        _method = method;
        _peer = peer;
    }

    protected override string MethodCore => _method;
    protected override string HostCore => "localhost";
    protected override string PeerCore => _peer;
    protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
    protected override Metadata RequestHeadersCore => _requestHeaders;
    protected override CancellationToken CancellationTokenCore => CancellationToken.None;
    protected override Metadata ResponseTrailersCore => _responseTrailers;
    protected override Status StatusCore { get; set; } = Status.DefaultSuccess;
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => _authContext;
    protected override IDictionary<object, object> UserStateCore => _userState;

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
        => throw new NotSupportedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
        => Task.CompletedTask;
}