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
	/// <summary>
	/// STUN服务器主端口,用于检测发来的消息是否是有效的,防止被恶意攻击,
	/// 如果用于主服务器,也用于第一次开始确认自己NAT的时候数据包的发送
	/// 如果用于从服务器,只是为了检测数据包的安全性
	/// 客户端->主STUN:这个端口->客户端
	///			↓
	///		   从STUN:这个端口->客户端
	/// </summary>
	public ushort STUNMainAndSlaveServerPrimaryPort { get; set; }
	/// <summary>
	/// NAT类型确认过程中,主服务器除了从主端口外,还会使用这个端口返回数据包,以确认NAT类型的端口限制与否
	/// 如果客户端收不到这个数据包,则说明相同IP的不同端口返回数据包的限制,也就是,至少是端口限制型NAT(还有可能是对称型NAT)
	/// 客户端->主STUN:主要端口->客户端
	///				   ↓
	///				 这个端口->客户端
	/// </summary>
	public ushort STUNMainServerSecondaryPort { get; set; }
	/// <summary>
	/// STUN从服务器的备用端口,主要用于检测发来的消息是否是有效的,防止被恶意攻击
	/// 客户端->主STUN:主端口->客户端
	///			↓
	///		  从STUN:这个端口->客户端
	///
	///
	/// 另外也会收到客户端发来的请求,用于客户端确认自己的NAT端口变化情况
	/// </summary>
	public ushort STUNSlaveServerSecondaryPort { get; set; }
	
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
		STUNMainAndSlaveServerPrimaryPort = 3478,
		STUNMainServerSecondaryPort = 3479,
		STUNSlaveServerSecondaryPort = 3480,
		TURNServerPrimaryPort = 3749,
		TURNServerAdditionalPortsForNATPortPrediction = 
			new List<ushort> {3750,3751,3752,3753,3754,3755,3756,3757,3758,3759},//额外10个,一共TURN 默认会有11个端口 
		TURNServerDataTransferPortFor2SymmetricNATClients = 3888
	};
}