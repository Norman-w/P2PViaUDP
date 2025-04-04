using System.Net;
using TURNServer;

public partial class TURNClient
{
	/// <summary>
	/// 客户端的公网连接信息(从STUN服务器得到的)
	/// </summary>
	public List<IPEndPoint> EndPointsFromSTUN { get; set; } = new();
	/// <summary>
	/// 客户端的公网连接信息(从TURN服务器得到的)
	/// </summary>
	public IPEndPoint EndPointFromTURN { get; init; }
	/// <summary>
	/// 客户端的Guid
	/// </summary>
	public Guid Guid { get; init; }
	/// <summary>
	/// 客户端的NAT类型
	/// </summary>
	public NATTypeEnum NATType { get; init; }
	/// <summary>
	/// 客户端的最后活动时间
	/// </summary>
	public DateTime LastActivityTime { get; set; } = DateTime.UtcNow;
}

public partial class TURNClient
{
	/// <summary>
	/// 客户端的IP和该IP上的所有端口的字典
	/// </summary>
	public Dictionary<string, List<IPEndPoint>> IpAndPortInThatIpDict
		=> EndPointsFromSTUN
			.GroupBy(x => x.Address.ToString())
			.ToDictionary(x => x.Key, x => x.ToList());
	public ushort ClientIpCountReplyFromSTUN => (ushort)IpAndPortInThatIpDict.Count;
}