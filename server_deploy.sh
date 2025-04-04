#!/bin/bash

# 服务器端部署脚本
# 设置颜色输出
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}=========================================================${NC}"
echo -e "${BLUE}=============== 服务器端部署开始执行 ===================${NC}"
echo -e "${BLUE}=========================================================${NC}\n"

# 获取参数
SERVICE_NAME=$1
BASE_DIR="/opt/P2PViaUdp"

# 如果没有指定服务名称，则退出
if [ -z "$SERVICE_NAME" ]; then
    echo -e "${RED}错误: 未指定服务名称${NC}"
    exit 1
fi

# 设置端口号
if [ "$SERVICE_NAME" == "STUNServer" ]; then
    PORT=3478
elif [ "$SERVICE_NAME" == "TURNServer" ]; then
    PORT=3749
else
    echo -e "${RED}错误: 无效的服务名称 $SERVICE_NAME${NC}"
    exit 1
fi

# 检查并清理临时文件
echo -e "${YELLOW}清理服务器上可能存在的临时文件...${NC}"
rm -f $BASE_DIR/p2p_deploy.tar.gz 2>/dev/null || true

# 检查端口占用并停止进程
echo -e "${YELLOW}检查端口 $PORT 是否被占用...${NC}"
if lsof -i udp:$PORT > /dev/null 2>&1; then
    echo -e "${YELLOW}端口 $PORT 被占用，正在停止相关进程...${NC}"
    PID=$(lsof -ti udp:$PORT)
    if [ ! -z "$PID" ]; then
        echo -e "${YELLOW}找到进程ID: $PID，正在终止...${NC}"
        kill -9 $PID
        sleep 2
        
        # 再次检查确认端口已释放
        if lsof -i udp:$PORT > /dev/null 2>&1; then
            echo -e "${RED}无法停止占用端口的进程，尝试强制终止...${NC}"
            killall -9 dotnet 2>/dev/null || true
            sleep 2
            
            # 最终确认
            if lsof -i udp:$PORT > /dev/null 2>&1; then
                echo -e "${RED}无法释放端口 $PORT，请手动检查${NC}"
                echo -e "${RED}执行: lsof -i udp:$PORT 查看占用情况${NC}"
                exit 1
            fi
        fi
        echo -e "${GREEN}成功停止占用端口的进程${NC}"
    else
        echo -e "${RED}无法找到占用端口的进程PID${NC}"
        echo -e "${RED}尝试使用killall命令...${NC}"
        killall -9 dotnet 2>/dev/null || true
        sleep 2
        
        # 检查端口是否释放
        if lsof -i udp:$PORT > /dev/null 2>&1; then
            echo -e "${RED}无法释放端口 $PORT，请手动检查${NC}"
            exit 1
        fi
        echo -e "${GREEN}端口已释放${NC}"
    fi
else
    echo -e "${GREEN}端口 $PORT 未被占用${NC}"
fi

# 设置权限
echo -e "${YELLOW}设置项目目录权限...${NC}"
chmod -R 755 $BASE_DIR
if [ -f "$BASE_DIR/$SERVICE_NAME/$SERVICE_NAME" ]; then
    chmod +x $BASE_DIR/$SERVICE_NAME/$SERVICE_NAME
fi

# 启动服务
echo -e "${YELLOW}启动 $SERVICE_NAME...${NC}"
cd $BASE_DIR/$SERVICE_NAME

echo -e "${BLUE}=========================================================${NC}"
echo -e "${BLUE}=============== 服务启动中，日志输出如下 ================${NC}"
echo -e "${BLUE}=========================================================${NC}\n"

echo -e "${GREEN}服务将在前台运行，按Ctrl+C可终止服务${NC}"
dotnet run

# 注意：当用户按Ctrl+C终止服务时，下面的代码不会执行
echo -e "${YELLOW}服务已停止，清理资源...${NC}"