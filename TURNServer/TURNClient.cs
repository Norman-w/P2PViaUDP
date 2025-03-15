using System.Net;

public class TURNClient
{
	/// <summary>
	/// 客户端的公网连接信息(从STUN服务器得到的)
	/// </summary>
	public IPEndPoint EndPointFromSTUN { get; set; }
	/// <summary>
	/// 客户端的公网连接信息(从TURN服务器得到的)
	/// </summary>
	public IPEndPoint EndPointFromTURN { get; set; }
	/// <summary>
	/// 客户端的Guid
	/// </summary>
	public Guid Guid { get; set; }
}