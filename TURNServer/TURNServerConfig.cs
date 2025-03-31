using P2PViaUDP;

namespace TURNServer;

public class TURNServerConfig : ConfigBase, IConfig
{
	/// <summary>
	/// TURN 服务器的主端口
	/// </summary>
	public ushort MainPort { get; private set; }
	/// <summary>
	/// TURN 服务器的额外端口集合,这些端口用于在执行P2P打洞的时候,客户端持续有规律的像TURN发送消息以确认客户端自己的端口变化情况
	/// 通过客户端的端口变化情况,TURN服务器会给客户端反馈他跟谁打洞的时候可能要使用的端口号
	/// 比如客户端A向客户端B(或任意其他IP)的多个端口打洞时(两端都是端口受限)
	/// TURN收到的客户端A的端口变化可能是:
	///		第1次 111.111.111.111:11111
	///		第2次 111.111.111.111:11112
	///		第3次 111.111.111.111:11113
	/// 那么TURN猜测 第4次请求,客户端A的端口可能是:111.111.111.111:11114
	/// TURN将这个信息发送给客户端B,B在进行打洞的时候就会按照这个端口尝试打洞.
	/// 直到有一条客户端和A客户端B都懵对了的时候,两者成功的建立连接
	/// 这些端口就是TURN用来确认客户端的端口变化情况的
	/// </summary>
	public List<ushort> AdditionalPortsForTURNPrediction { get; private set; } = new();
	
	public ushort TURNServerDataTransferPortFor2SymmetricNATClients { get; private set; }
	/// <summary>
	/// 用于检测NAT类型一致性的端口
	/// 当尝试发起打洞或者是抛出橄榄枝后,客户端请求这个端口,TURN得知他的出网地址后告诉客户端.
	/// 客户端通过这个端口跟之前他自己记录的出网NAT进行对比,看看自己的端口变化了没有,如果变化了,如果端口受限型,那打洞肯定是会失败的
	/// </summary>
	public ushort NATTypeConsistencyKeepingCheckingPort { get; private set; }
	
	public static TURNServerConfig Default => new()
	{
		MainPort = 3749,//注意是3749不是3479
		NATTypeConsistencyKeepingCheckingPort = 3750,
		TURNServerDataTransferPortFor2SymmetricNATClients = 3888
	};
}