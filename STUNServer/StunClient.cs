using System.Net;

namespace STUNServer;

public class StunClient
{
	public StunClient(IPEndPoint initialClientEndPoint)
	{
		InitialClientEndPoint = initialClientEndPoint;
	}
	public Guid Id { get; private set; } = Guid.NewGuid();
	public IPEndPoint InitialClientEndPoint { get; private set; }
	public DateTime? LastToServerTime { get; private set; }
	public DateTime? LastToClientTime { get; private set; }
	public DateTime LastActivity { get; set; } = DateTime.UtcNow;

}