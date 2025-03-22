using P2PViaUDP;

namespace STUNServer;

public class STUNServerConfig : ConfigBase, IConfig
{
	private STUNServerConfig(string mainServerInternalIP, string slaveServerInternalIP)
	{
		MainServerInternalIP = mainServerInternalIP;
		SlaveServerInternalIP = slaveServerInternalIP;
	}

	/// <summary>
	/// 是否是从服务器
	/// </summary>
	public bool IsSlaveServer { get; set; }

	/// <summary>
	/// 主服务器的主要端口, 默认3478
	/// </summary>
	public ushort MainServerAndSlaveServerPrimaryPort { get; private set; }

	/// <summary>
	/// 主服务器的次要端口, 默认3479
	/// </summary>
	public ushort MainServerSecondaryPort { get; private set; }

	/// <summary>
	/// 从服务器的主要端口, 默认3478
	/// </summary>
	public ushort SlaveServerPrimaryPort { get; private set; }

	/// <summary>
	/// 从服务器的次要端口, 默认3480
	/// </summary>
	public ushort SlaveServerSecondaryPort { get; private set; }

	/// <summary>
	/// 主服务器的内网IP
	/// </summary>
	public string MainServerInternalIP { get; private set; }

	/// <summary>
	/// 从服务器的内网IP
	/// </summary>
	public string SlaveServerInternalIP { get; private set; }
	/// <summary>
	/// 从服务器接收主服务器的数据包的端口
	/// 主服务器收到客户端的STUN请求后,会把客户端的公网IP和端口发送给从服务器,让从服务器从 主从 两个端口分别返回数据, 以确认NAT类型的IP限制与否
	/// 为了讲求一个"快" 主从服务器之间使用UDP协议
	/// </summary>
	public ushort SlaveServerReceiveMainServerBytesPort { get; private set; }

	public static STUNServerConfig Default => new("192.168.6.200", "192.168.1.252")
	{
		IsSlaveServer = false,
		MainServerAndSlaveServerPrimaryPort = 3478,
		MainServerSecondaryPort = 3479,
		SlaveServerPrimaryPort = 3478,
		SlaveServerSecondaryPort = 3480,
		SlaveServerReceiveMainServerBytesPort = 3500
	};
}