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
	/// 主服务器用于检测客户端是哪种锥形的网络
	/// 服务端收到消息以后除了从这个端口的链接返回,还会转发到自己的哪种锥形监测的另外一个端口以及从服务器的哪种锥形监测的主从端口进行返回
	/// 客户端根据收到的响应来确认自己是什么型的网络
	/// </summary>
	public ushort MainServerWhichKindOfConeRequestAndResponsePort { get; private set; }
	/// <summary>
	/// 主服务器的用于检测客户端是哪种锥形的网络的应答时使用的次要端口,主服务器接受到检测请求后也会经由这里返回数据给客户端,不接收.
	/// </summary>
	public ushort MainServerWhichKindOfConeResponseSecondaryPort { get; private set; }
	/// <summary>
	/// 从服务器用于检测客户端是哪种锥形的网络的主要端口,得到主服务器的透传信号后,用于返回数据给客户端,不接收.
	/// </summary>
	public ushort SlaveServerWhichKindOfConeResponsePrimaryPort { get; private set; }
	/// <summary>
	/// 从服务器用于检测客户端是哪种锥形的网络的次要端口,得到主服务器的透传信号后,用于返回数据给客户端,不接收.
	/// </summary>
	public ushort SlaveServerWhichKindOfConeResponseSecondaryPort { get; private set; }

	/// <summary>
	/// 主服务器用来接受并返回给客户端的用于检查"是否对称型NAT"的主接口
	/// </summary>
	public ushort MainServerIsSymmetricRequestAndResponsePrimaryPort { get; private set; }
	public ushort MainServerIsSymmetricRequestAndResponseSecondaryPort { get; private set; }
	public ushort SlaveServerIsSymmetricRequestAndResponsePrimaryPort { get; private set; }
	public ushort SlaveServerIsSymmetricRequestAndResponseSecondaryPort { get; private set; }
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

		#region 用于检测"是哪种锥形"的端口设置

		MainServerWhichKindOfConeRequestAndResponsePort = 3478,
		MainServerWhichKindOfConeResponseSecondaryPort = 3479,
		SlaveServerWhichKindOfConeResponsePrimaryPort = 3480,
		SlaveServerWhichKindOfConeResponseSecondaryPort = 3481,

		#endregion

		#region 用于检测"是否对称型"的端口设置

		MainServerIsSymmetricRequestAndResponsePrimaryPort = 3482,
		MainServerIsSymmetricRequestAndResponseSecondaryPort = 3483,
		SlaveServerIsSymmetricRequestAndResponsePrimaryPort = 3484,
		SlaveServerIsSymmetricRequestAndResponseSecondaryPort = 3485,

		#endregion
		
		SlaveServerReceiveMainServerBytesPort = 3500
	};
}