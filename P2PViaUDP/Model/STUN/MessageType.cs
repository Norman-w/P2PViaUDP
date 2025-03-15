namespace P2PViaUDP.Model;

public enum MessageType
{
	/// <summary>
	/// STUN请求
	/// </summary>
	StunRequest,
	/// <summary>
	/// STUN响应
	/// </summary>
	StunResponse,
	/// <summary>
	/// STUN响应错误
	/// </summary>
	StunResponseError
}