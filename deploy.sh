#!/bin/bash

# 设置颜色输出
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 服务器信息
SERVER="root@norman.wang"
REMOTE_DIR="/opt/P2PViaUdp"
REMOTE_SCRIPT="server_deploy.sh"

# 检查server_deploy.sh是否存在
if [ ! -f "$REMOTE_SCRIPT" ]; then
    echo -e "${RED}错误: 服务器端脚本 $REMOTE_SCRIPT 不存在${NC}"
    exit 1
fi

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

# 创建临时目录
TEMP_DIR=$(mktemp -d)
echo -e "${YELLOW}创建临时打包目录: $TEMP_DIR${NC}"

# 打包服务项目和P2PViaUDP项目
echo -e "${YELLOW}打包项目文件...${NC}"
mkdir -p $TEMP_DIR/$SERVICE_NAME
mkdir -p $TEMP_DIR/P2PViaUDP

# 复制服务项目文件(排除bin和obj)
echo -e "${YELLOW}复制$SERVICE_NAME项目文件...${NC}"
rsync -a --exclude 'bin/' --exclude 'obj/' --exclude '.vs/' --exclude '.git/' $SERVICE_NAME/ $TEMP_DIR/$SERVICE_NAME/

# 复制P2PViaUDP项目文件(如果存在)
if [ -d "P2PViaUDP" ]; then
    echo -e "${YELLOW}复制P2PViaUDP项目文件...${NC}"
    rsync -a --exclude 'bin/' --exclude 'obj/' --exclude '.vs/' --exclude '.git/' P2PViaUDP/ $TEMP_DIR/P2PViaUDP/
fi

# 复制服务器部署脚本
cp $REMOTE_SCRIPT $TEMP_DIR/

# 打包所有文件
echo -e "${YELLOW}打包所有文件...${NC}"
cd $TEMP_DIR
tar -czf p2p_deploy.tar.gz $SERVICE_NAME P2PViaUDP $REMOTE_SCRIPT

# 显示文件大小
FILESIZE=$(du -h p2p_deploy.tar.gz | cut -f1)
echo -e "${YELLOW}打包完成，文件大小: ${FILESIZE}${NC}"

# 上传压缩包到服务器
echo -e "${YELLOW}上传文件到服务器...${NC}"
sshpass -p "$SERVER_PASS" ssh -o StrictHostKeyChecking=no $SERVER "mkdir -p $REMOTE_DIR"
sshpass -p "$SERVER_PASS" rsync -avz --progress --stats $TEMP_DIR/p2p_deploy.tar.gz $SERVER:$REMOTE_DIR/

# 在本地完成后显示分割线
echo -e "\n${BLUE}=========================================================${NC}"
echo -e "${BLUE}============== 本地操作完成，开始远程部署 ==============${NC}"
echo -e "${BLUE}=========================================================${NC}\n"

# 在服务器上触发服务器端脚本
echo -e "${YELLOW}在服务器上启动部署...${NC}"
sshpass -p "$SERVER_PASS" ssh -t -o StrictHostKeyChecking=no $SERVER "cd $REMOTE_DIR && tar -xzf p2p_deploy.tar.gz && bash $REMOTE_SCRIPT $SERVICE_NAME && rm -f p2p_deploy.tar.gz"

# 清理临时文件
echo -e "${YELLOW}清理本地临时文件...${NC}"
rm -rf $TEMP_DIR

# 清除密码变量
unset SERVER_PASS

echo -e "${GREEN}部署完成${NC}"