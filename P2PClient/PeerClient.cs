using System.Net;

namespace P2PClient;

public partial class PeerClient
{
	/// <summary>
	/// 判断是否已经建立了P2P
	/// </summary>
	public bool IsP2PHasBeenEstablished => FirstP2PAvailableTime != null;
}

/// <summary>
/// 跟我相互打洞的客户端类
/// </summary>
public partial class PeerClient
{
	public PeerClient(IPEndPoint endPoint)
	{
		EndPoint = endPoint;
	}

	/// <summary>
	/// 客户端的Guid
	/// </summary>
	public Guid Guid { get; set; }

	/// <summary>
	/// 他的公网信息
	/// </summary>
	public IPEndPoint EndPoint { get; set; }

	/// <summary>
	/// 最后一次我发给他的心跳时间,如果还没发过则为null
	/// </summary>
	public DateTime? LastHeartbeatToHim { get; set; }

	/// <summary>
	/// 最后一次他发给我的心跳时间,如果还没收到过则为null
	/// </summary>
	public DateTime? LastHeartbeatFromHim { get; set; }

	/// <summary>
	/// 最后一次我收到他的时间,如果还没收到过则为null
	/// </summary>
	public DateTime? LastReceiveTime { get; set; }

	/// <summary>
	/// 最后一次我发送给他的时间,如果还没发送过则为null
	/// </summary>
	public DateTime? LastSendTime { get; set; }

	/// <summary>
	/// 首次P2P可用时间,我给他发过消息并且他也给我回过心跳的第一次设置一整个时间
	/// </summary>
	public DateTime? FirstP2PAvailableTime { get; set; }
}