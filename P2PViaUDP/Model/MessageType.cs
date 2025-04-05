namespace P2PViaUDP.Model;

public enum MessageType
{
	/// <summary>
	/// STUN请求
	/// </summary>
	StunRequest,
	StunNATTypeCheckingRequest,
	StunNATTypeCheckingResponse,
	/// <summary>
	/// STUN响应
	/// </summary>
	StunResponse,
	/// <summary>
	/// STUN响应错误
	/// </summary>
	StunResponseError,
	/// <summary>
	/// TURN广播,由TURN服务器广播给各个在组中的客户端,告诉他们有新的客户端加入
	/// TURN发送给这个[新客户端]-> : 你可以尝试和[你的前辈们]打洞
	/// TURN发送给[所有之前的客户端]-> : 你们可以尝试和[新客户端]打洞
	/// </summary>
	TURNBroadcast,
	/// <summary>
	/// TURN注册,由客户端发送给TURN服务器,告诉TURN服务器自己的公网终结点,公网终结点来自于STUN服务器的响应
	/// 注册后TURN会将客户端加入组,并开始广播操作
	/// </summary>
	TURNRegister,
	/// <summary>
	/// TURN心跳,由TURN服务器发送给客户端,告诉客户端自己还活着
	/// </summary>
	TURNServer2ClientHeartbeat,
	/// <summary>
	/// TURN心跳,由客户端发送给TURN服务器,告诉TURN服务器自己还活着
	/// </summary>
	TURNClient2ServerHeartbeat,
	/// <summary>
	/// P2P打洞请求
	/// </summary>
	P2PHolePunchingRequest,
	/// <summary>
	/// P2P打洞响应
	/// </summary>
	P2PHolePunchingResponse,
	/// <summary>
	/// P2P心跳,由客户端发送给客户端,告诉对方自己还活着 双方是直接通信的
	/// </summary>
	P2PHeartbeat,
	/// <summary>
	/// TURN检查NAT一致性请求
	/// </summary>
	TURNCheckNATConsistencyRequest,
	/// <summary>
	/// TURN检查NAT一致性响应
	/// </summary>
	TURNCheckNATConsistencyResponse,
}