# P2P基于UDP的打洞套件

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

- [ ] STUN & TURN服务器在正式环境,一客户机在联通4G-WIFI棒下的客户端,一客户机在开发电脑中国电信宽带,不能正常工作**
