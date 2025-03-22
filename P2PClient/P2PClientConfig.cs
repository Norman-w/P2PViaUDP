using P2PViaUDP;

namespace P2PClient;

public class P2PClientConfig : ConfigBase, IConfig
{
	private P2PClientConfig(string stunMainServerIP, string stunSlaveServerIP)
	{
		STUNMainServerIP = stunMainServerIP;
		STUNSlaveServerIP = stunSlaveServerIP;
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
	public static P2PClientConfig Default => new("97.64.24.135", "121.22.36.190")
	{
		STUNMainAndSlaveServerPrimaryPort = 3478
	};
}