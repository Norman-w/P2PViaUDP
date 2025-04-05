#!/bin/bash

# 设置颜色输出
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 从.env.development文件读取环境变量
ENV_FILE=".env.development"
if [ -f "$ENV_FILE" ]; then
    echo -e "${YELLOW}从 $ENV_FILE 读取环境变量...${NC}"
    
    # 逐行读取环境变量，处理注释、引号和空格
    while IFS= read -r line || [ -n "$line" ]; do
        # 跳过空行和完全由注释组成的行
        if [[ -z "$line" || "$line" =~ ^[[:space:]]*# ]]; then
            continue
        fi
        
        # 提取变量名和值，去除注释部分
        if [[ "$line" =~ ^[[:space:]]*([A-Za-z0-9_]+)[[:space:]]*=[[:space:]]*(.*) ]]; then
            key="${BASH_REMATCH[1]}"
            value="${BASH_REMATCH[2]}"
            
            # 去除可能存在的注释
            value=$(echo "$value" | sed -E 's/#.*//g')
            
            # 去除首尾空格
            value=$(echo "$value" | sed -E 's/^[[:space:]]+|[[:space:]]+$//g')
            
            # 去除可能存在的引号
            value=$(echo "$value" | sed -E 's/^["\047]|["\047]$//g')
            
            # 设置环境变量
            declare "$key=$value"
            echo -e "设置环境变量: ${GREEN}$key${NC}"
        fi
    done < "$ENV_FILE"
    
    # 使用环境变量设置服务器信息
    if [ ! -z "$SERVER_USERNAME" ] && [ ! -z "$SERVER_ADDRESS" ]; then
        SERVER="${SERVER_USERNAME}@${SERVER_ADDRESS}"
        if [ ! -z "$SERVER_PORT" ]; then
            SSH_PORT_OPTION="-p $SERVER_PORT"
        fi
        echo -e "${GREEN}从环境变量设置服务器: $SERVER${NC}"
    else
        echo -e "${YELLOW}未找到服务器信息环境变量，使用默认值${NC}"
        SERVER="root@norman.wang"
    fi
else
    echo -e "${YELLOW}未找到 $ENV_FILE 文件，使用默认设置${NC}"
    SERVER="root@norman.wang"
fi

REMOTE_DIR="/tmp/P2PViaUdp"
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

# 如果环境变量中没有密码，则提示输入
if [ -z "$SERVER_PASSWORD" ]; then
    read -s -p "请输入服务器密码: " SERVER_PASSWORD
    echo
fi

# 测试连接
echo -e "${YELLOW}测试服务器连接...${NC}"
if ! sshpass -p "$SERVER_PASSWORD" ssh -o StrictHostKeyChecking=no $SSH_PORT_OPTION $SERVER "echo '连接成功'" &> /dev/null; then
    echo -e "${RED}连接失败，请检查服务器地址和密码${NC}"
    exit 1
fi
echo -e "${GREEN}连接成功${NC}"

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
sshpass -p "$SERVER_PASSWORD" ssh -o StrictHostKeyChecking=no $SSH_PORT_OPTION $SERVER "mkdir -p $REMOTE_DIR"
sshpass -p "$SERVER_PASSWORD" rsync -avz --progress --stats $TEMP_DIR/p2p_deploy.tar.gz $SERVER:$REMOTE_DIR/

# 上传完成后立即清理本地临时文件
echo -e "${YELLOW}清理本地临时文件...${NC}"
rm -rf $TEMP_DIR

# 在本地完成后显示分割线
echo -e "\n${BLUE}=========================================================${NC}"
echo -e "${BLUE}============== 本地操作完成，开始远程部署 ==============${NC}"
echo -e "${BLUE}=========================================================${NC}\n"

# 在服务器上触发服务器端脚本
echo -e "${YELLOW}在服务器上启动部署...${NC}"
sshpass -p "$SERVER_PASSWORD" ssh -t -o StrictHostKeyChecking=no $SSH_PORT_OPTION $SERVER "cd $REMOTE_DIR && tar -xzf p2p_deploy.tar.gz && rm -f p2p_deploy.tar.gz && bash $REMOTE_SCRIPT $SERVICE_NAME"

# 清除密码变量
unset SERVER_PASSWORD

echo -e "${GREEN}部署完成${NC}"