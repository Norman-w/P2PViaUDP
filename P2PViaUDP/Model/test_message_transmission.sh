#!/bin/bash

# 测试1：发送简单消息
echo "测试1：发送简单消息"
echo -n "Hello" | nc -u 127.0.0.1 3478
sleep 1

# 测试2：发送较长消息
echo "测试2：发送较长消息"
echo -n "这是一个较长的测试消息" | nc -u 127.0.0.1 3478
sleep 1

# 测试3：发送特殊字符
echo "测试3：发送特殊字符"
echo -n "Hello!@#$%^&*()" | nc -u 127.0.0.1 3478