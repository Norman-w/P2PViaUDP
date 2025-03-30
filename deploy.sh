#!/bin/bash

# 设置颜色输出
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# 服务器信息
SERVER="root@norman.wang"
REMOTE_DIR="/opt/P2PViaUdp"

# 检查sshpass
if ! command -v sshpass &> /dev/null; then
    echo -e "${RED}错误: 未安装sshpass，请先安装${NC}"
    echo "在macOS上可以使用: brew install sshpass"
    exit 1
fi

# 获取密码
read -s -p "请输入服务器密码: " SERVER_PASS
echo

# 测试连接
echo -e "${YELLOW}测试服务器连接...${NC}"
if ! sshpass -p "$SERVER_PASS" ssh -o StrictHostKeyChecking=no $SERVER "echo '连接成功'" &> /dev/null; then
    echo -e "${RED}连接失败，请检查服务器地址和密码${NC}"
    exit 1
fi
echo -e "${GREEN}连接成功${NC}"

# 选择服务
echo -e "${YELLOW}请选择要启动的服务:${NC}"
echo "1) STUNServer"
echo "2) TURNServer"
read -p "请输入选项 (1-2): " choice

# 设置服务名称
case $choice in
    1)
        SERVICE_NAME="STUNServer"
        ;;
    2)
        SERVICE_NAME="TURNServer"
        ;;
    *)
        echo -e "${RED}无效的选项${NC}"
        exit 1
        ;;
esac

# 检查本地目录
if [ ! -d "$SERVICE_NAME" ]; then
    echo -e "${RED}错误: 本地目录 $SERVICE_NAME 不存在${NC}"
    exit 1
fi

# 创建远程目录
echo -e "${YELLOW}创建远程目录...${NC}"
sshpass -p "$SERVER_PASS" ssh -o StrictHostKeyChecking=no $SERVER "mkdir -p $REMOTE_DIR/$SERVICE_NAME"

# 复制P2PViaUDP项目（如果存在）
if [ -d "P2PViaUDP" ]; then
    echo -e "${YELLOW}复制P2PViaUDP项目...${NC}"
    sshpass -p "$SERVER_PASS" rsync -avz --progress \
        --exclude 'bin/' \
        --exclude 'obj/' \
        --exclude '.vs/' \
        --exclude '.git/' \
        P2PViaUDP/ \
        $SERVER:$REMOTE_DIR/P2PViaUDP/
fi

# 复制服务项目
echo -e "${YELLOW}复制$SERVICE_NAME项目...${NC}"
sshpass -p "$SERVER_PASS" rsync -avz --progress \
    --exclude 'bin/' \
    --exclude 'obj/' \
    --exclude '.vs/' \
    --exclude '.git/' \
    $SERVICE_NAME/ \
    $SERVER:$REMOTE_DIR/$SERVICE_NAME/

# 启动应用
echo -e "${YELLOW}正在启动应用...${NC}"
sshpass -p "$SERVER_PASS" ssh -o StrictHostKeyChecking=no $SERVER "cd $REMOTE_DIR/$SERVICE_NAME && dotnet run"

# 清除密码变量
unset SERVER_PASS 