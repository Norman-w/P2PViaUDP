using P2PViaUDP;

namespace P2PClient;

public class P2PClientConfig : ConfigBase, IConfig
{
	private P2PClientConfig(string stunMainServerIP, string stunSlaveServerIP, string turnServerIP)
	{
		STUNMainServerIP = stunMainServerIP;
		STUNSlaveServerIP = stunSlaveServerIP;
		TURNServerIP = turnServerIP;
	}
	/// <summary>
	/// STUN主服务器IP,开始确认自己的NAT类型时,会向这个服务器的主端口发包
	/// </summary>
	public string STUNMainServerIP { get; set; }
	/// <summary>
	/// STUN从服务器IP,当主服务器收到了客户端的请求后,会把客户端的公网IP和端口发送给从服务器,让从服务器从 主从 两个端口分别返回数据, 以确认NAT类型的IP限制与否
	/// 如果主从端口发回去的数据包客户端都没有收到,那么至少是IP限制型NAT(还有可能是端口限制型NAT或对称型NAT)
	///
	/// 另外,从从服务器后期也会接收到和主服务器一样收到的消息,用于客户端确认自己出网时候的NAT端口变化
	/// </summary>
	public string STUNSlaveServerIP { get; set; }
	public ushort STUNWitchKindOfConeServerPort { get; set; }
	
	public ushort STUNWhichKindOfConeMainServerRequestAndResponsePort { get; set; }
	public ushort STUNWhichKindOfConeMainServerResponseSecondaryPort { get; set; }
	public ushort STUNWhichKindOfConeSlaveServerResponsePrimaryPort { get; set; }
	public ushort STUNWhichKindOfConeSlaveServerResponseSecondaryPort { get; set; }
	
	public ushort STUNIsSymmetricMainServerPrimaryPort { get; set; }
	public ushort STUNIsSymmetricMainServerSecondaryPort { get; set; }
	public ushort STUNIsSymmetricSlaveServerPrimaryPort { get; set; }
	public ushort STUNIsSymmetricSlaveServerSecondaryPort { get; set; }
	
	
	/// <summary>
	/// TURN服务端的IP
	/// </summary>
	public string TURNServerIP { get; set; }
	/// <summary>
	/// TURN服务端的主端口,用于向TURN客户端注册自己等
	/// </summary>
	public ushort TURNServerPrimaryPort { get; set; }
	/// <summary>
	/// TURN服务端的额外端口集合,这些端口用于在执行P2P打洞的时候,客户端持续有规律的像TURN发送消息以确认客户端自己的端口变化情况
	/// 如果端口测算由TURN服务器进行,则不需要从这些地方给客户端返回信息,而是把测算的信息从TURNServerPrimaryPort返回.
	/// </summary>
	public List<ushort> TURNServerAdditionalPortsForNATPortPrediction { get; set; } = new();
	/// <summary>
	/// 当两个打洞的客户端都是对称型NAT时,客户端们通过这个端口进行数据的传输.也就是中继端口.这个端口流量会是整个系统中最大的.
	/// </summary>
	public ushort TURNServerDataTransferPortFor2SymmetricNATClients { get; set; }
	public static P2PClientConfig Default => new("97.64.24.135", "121.22.36.190", "97.64.24.135")
	{
		STUNWitchKindOfConeServerPort = 3478,
		/*
		 
		 #region 用于检测"是否对称型"的端口设置

		   MainServerIsSymmetricRequestAndResponsePrimaryPort = 3482,
		   MainServerIsSymmetricRequestAndResponseSecondaryPort = 3483,
		   SlaveServerIsSymmetricRequestAndResponsePrimaryPort = 3484,
		   SlaveServerIsSymmetricRequestAndResponseSecondaryPort = 3485,

		   #endregion
		
		
		*/
		STUNIsSymmetricMainServerPrimaryPort = 3482,
		STUNIsSymmetricMainServerSecondaryPort = 3483,
		STUNIsSymmetricSlaveServerPrimaryPort = 3484,
		STUNIsSymmetricSlaveServerSecondaryPort = 3485,
		
		
		/*
		 
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
		
		
		*/
		STUNWhichKindOfConeMainServerRequestAndResponsePort = 3478,
		STUNWhichKindOfConeMainServerResponseSecondaryPort = 3479,
		STUNWhichKindOfConeSlaveServerResponsePrimaryPort = 3480,
		STUNWhichKindOfConeSlaveServerResponseSecondaryPort = 3481,
		
		
		TURNServerPrimaryPort = 3749,
		TURNServerAdditionalPortsForNATPortPrediction = 
			new List<ushort> {3750,3751,3752,3753,3754,3755,3756,3757,3758,3759},//额外10个,一共TURN 默认会有11个端口 
		TURNServerDataTransferPortFor2SymmetricNATClients = 3888
	};
}