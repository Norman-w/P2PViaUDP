# P2P基于UDP的打洞套件

## 开始开发:

* 装好.net sdk, 大于6.0以上都可以,比如只有.net7.0,将项目临时改成net7.0即可
* 可选装好docker,方便在docker中运行客户端
* 准备一台服务器,开通所有本项目中的UDP端口的入网流量,建议直接设置3478 ~ 3577区间,从服务器出网建议不限制.
* 可选创建.env.development文件,并在其中设置环境变量(直接粘贴下方内容即可):
```bash
#开发时的环境变量设置,方便运行部署脚本,不用输入服务端的用户名密码,快捷高效.
#注意这个文件的当前版本(模板)可以上传到服务器上,但真实内容不可以.该文件上传后已经设置了gitignore.

SERVER_ADDRESS = "" #服务器地址
SERVER_USERNAME = "" #服务器用户名
SERVER_PASSWORD = "" #服务器密码
SERVER_PORT = "" #服务器端口
```
* **两个**终端分别运行
```bash 
./deploy.sh
```
分别选择1(STUN)和2(TURN)进行部署.
* 新开**两个**终端运行
```bash
./run-test-p2p-client-in-docker.sh
```
进行测试 , 或者直接在终端中运行 
```bash
dotnet run --project P2PClient/P2PClient.csproj
``` 
启动客户端, 该客户端会自动连接到STUN和TURN服务器,并进行打洞测试.
* 从STUN,TURN,两个客户端的终端分别观察日志了解基本流程.

## 提笔忘字
客户端发送到STUN服务端消息确认自己NAT类型时,我将设计成多种发送和接收方式:
* 4 Responses:客户端发送给主STUN的主端口,然后主STUN转发给从STUN, 主从服务器各自从主从端口一共4个返回数据包给客户端
* 2 Responses:客户端发送给主STUN的主端口和从STUN的主端口,确认发送到不同的IP相同的端口的NAT信息
* 2 Responses:客户端发送给主STUN的主端口和从端口,确认发送到相同IP不同端口的NAT信息
* 10秒内 Responses:客户端在打洞过程中持续的也向从STUN发送信息,确认他的网络变化情况
  * 比如原来是全锥形的,会不会有变化影响了打洞
  * 比如是端口受限的,那么我们可以通过STUN `或者这个应该由TURN服务器来完成,因为他负责分发双方信息(有客户端ID),知道对方是不是活的要不要继续打了` 收到的端口的变化规律来告诉他如果下一次客户端再出网可能会变成什么端口
    * 如每次递增NAT外网端口号16,那么下一次可能是当前端口号+16
    * 如历史递增NAT外网端口号差额分别是2,2,4,4 那么下一次可能是当前端口号+4
    * 如历史递增端口号的差额分别是2, -111, 22232, 2 那么下一次的基本无法预测,所以我们只能告诉他下一次的端口号是随机的(基本可以放弃端口预测打洞)

来自华为的解释:
https://info.support.huawei.com/info-finder/encyclopedia/zh/NAT.html

#### Full Cone NAT（完全锥型NAT）

  所有从同一个私网IP地址和端口（IP1:Port1）发送过来的请求都会被映射成同一个公网IP地址和端口（IP:Port）。并且，任何外部主机通过向映射的公网IP地址和端口发送报文，都可以实现和内部主机进行通信。

  这是一种比较宽松的策略，只要建立了私网IP地址和端口与公网IP地址和端口的映射关系，所有的Internet上的主机都可以访问该NAT之后的主机。

#### Restricted Cone NAT（限制锥型NAT）

  所有从同一个私网IP地址和端口（IP1:Port1）发送过来的请求都会被映射成同一个公网IP和端口号（IP:Port）。与完全锥型NAT不同的是，当且仅当内部主机之前已经向公网主机发送过报文，此时公网主机才能向私网主机发送报文。

#### Port Restricted Cone NAT（端口限制锥型NAT）

  与限制锥型NAT很相似，只不过它包括端口号。也就是说，一台公网主机（IP2:Port2）想给私网主机发送报文，必须是这台私网主机先前已经给这个IP地址和端口发送过报文。

#### Symmetric NAT（对称NAT）

  所有从同一个私网IP地址和端口发送到一个特定的目的IP地址和端口的请求，都会被映射到同一个IP地址和端口。如果同一台主机使用相同的源地址和端口号发送报文，但是发往不同的目的地，NAT将会使用不同的映射。此外，只有收到数据的公网主机才可以反过来向私网主机发送报文。

  这和端口限制锥型NAT不同，端口限制锥型NAT是所有请求映射到相同的公网IP地址和端口，而对称NAT是不同的请求有不同的映射。



## NAT类型:(该部分内容尚待重复验证)
###### _出发点指的相同一个P2PClient进程中,一个new UdpClient_
* 全锥形NAT:`客户端在访问服务器时,NAT设备会为客户端分配一个公网端口,这个端口可以由任何外部主机的任何端口访问`
  * 相同出发点,不同目标点:分配同到同一个出网时的公网端口
    * 相同出发点,同一服务器的不同端口:分配同到同一个出网时的公网端口
    * 相同出发点,不同服务器的相同端口:分配同到同一个出网时的公网端口
    * 相同出发点,不同服务器的不同端口:分配同到同一个出网时的公网端口
  * 不同出发点,相同目标点:各自分配不同的公网端口(端口号变化有规律) `⏳这个还没有测试过,是推测`
  * 不同出发点,不同目标点:各自分配不同的公网端口(端口号变化有规律)
* 地址限制型NAT:`只有客户端访问过的IP地址才能访问客户端的这个NAT外网端口,但是回信时使用的端口不限制`
  * 相同出发点,不同目标点:分配到不同的出网时的公网端口
  * 不同出发点,相同目标点:分配到不同的出网时的公网端口
* 端口限制型NAT:`只有客户端访问过的IP地址才能访问客户端的这个NAT外网端口,但是回信时使用的端口也要这个客户端之前访问过的`
* 对称型NAT:(根据现在测试看,端口非常随机)`只有客户端访问过的IP地址和端口才能访问客户端的这个NAT外网端口,比如自己的NAT是111.222.111.222:3333,访问的是111.111.111.111:4444,那么返回消息必须从111.111.111.111:4444发出去,这就表明如果我们111.111.111.111这个地址内网的主机如果不是全锥形的,端口稍微一变化,前者就无法正常通讯了,因为我们发不回去`
  * 相同出发点,不同目标点:分配到不同的出网时的公网端口
    * 相同出发点,同一服务器的不同端口:分配到不同的出网时的公网端口
    * 相同出发点,不同服务器的相同端口:分配到不同的出网时的公网端口
    * 相同出发点,不同服务器的不同端口:分配到不同的出网时的公网端口
  * 不同出发点,相同目标点:各自分配不同的公网端口
  * 不同出发点,不同目标点:各自分配不同的公网端口

## 图标:
* 锥形相关NAT:
  * 全锥形NAT:🎉
  * 地址限制型NAT:🌐
  * 端口限制型NAT:🧮
* 对称型NAT:🛡

运行测试客户端的sh脚本前请:
```bash
chmod +x run-docker.sh
```
再
```bash
./run-test-p2p-client-in-docker.sh
```

全锥形网络的拓扑参考:
```bash
d2 --watch udp-nat-p2p.d2
```
但是请注意在多数网络中运行时他并不一定能够起作用，因为大多数网络都是对称NAT，而不是全锥形NAT。
- [x] 在127.0.0.1环境下完全正常工作
- [x] STUN & TURN服务器在正式环境,两个客户机在同一个开发电脑正常工作(中国电信宽带)
- [x] 两个STUN服务端在不同的服务器,两个客户端在不同的网络环境下可正常检测出客户端为全锥形NAT
- [x] 💐完成了完整的NAT类型检测功能在STUN服务器中💐
- [ ] STUN & TURN服务器在正式环境,一客户机在联通4G-WIFI棒下的客户端,一客户机在开发电脑中国电信宽带,不能正常工作**
