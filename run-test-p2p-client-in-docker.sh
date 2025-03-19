#!/bin/bash
  
  # 定义颜色代码
  RED='\033[0;31m'
  GREEN='\033[0;32m'
  YELLOW='\033[1;33m'
  CYAN='\033[0;36m'
  NC='\033[0m' # No Color
  
  # 定义镜像名称和路径
  IMAGE_NAME="p2p-client"
  DOCKERFILE_PATH="P2PClient/Dockerfile"
  
  # 输出带颜色的信息函数
  print_message() {
      color=$1
      message=$2
      echo -e "${color}${message}${NC}"
  }
  
  # 错误处理函数
  handle_error() {
      print_message "${RED}" "错误: $1"
      exit 1
  }
  
  # 检查上一个命令是否成功
  check_status() {
      if [ $? -ne 0 ]; then
          handle_error "$1"
      fi
  }
  
  # 检查目录结构
  if [ ! -f "${DOCKERFILE_PATH}" ]; then
      print_message "${YELLOW}" "未找到Dockerfile，尝试切换到上级目录..."
      cd ..
      if [ ! -f "${DOCKERFILE_PATH}" ]; then
          handle_error "找不到Dockerfile，请确保在正确的目录中运行此脚本"
      fi
  fi
  
  # 1. 构建Docker镜像
  print_message "${CYAN}" "开始构建Docker镜像..."
  print_message "${CYAN}" "当前工作目录: $(pwd)"
  docker build . -f ${DOCKERFILE_PATH} -t ${IMAGE_NAME}
  check_status "Docker镜像构建失败"
  
  # 2. 运行Docker容器
  print_message "${GREEN}" "开始运行Docker容器..."
  docker run --rm --network host ${IMAGE_NAME}
  check_status "Docker容器运行失败"
  
  # 3. 清理Docker镜像
  print_message "${YELLOW}" "正在清理Docker镜像..."
  docker rmi ${IMAGE_NAME}
  if [ $? -ne 0 ]; then
      print_message "${YELLOW}" "警告: 镜像清理失败，可能已被删除或不存在"
  fi
  
  print_message "${GREEN}" "所有操作已完成"