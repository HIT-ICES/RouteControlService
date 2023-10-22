# RouteControlService

服务管理

## 配置

程序的包含如下主要配置，位于`appsettings.json`中:

```yaml
Dbms: mysql # 活动的connectionstring配置
MYSQL_IP: localhost # mysql的主机，需要开放3306
MYSQL_UID: mcsdbg # mysql用户名
MYSQL_PWD: MyWceQc-cFgPynao # mysql密码
LISTER_PORT: 80 # 工作端口
```

**可以直接使用环境变量来覆盖这些配置。**



## 构建

可以在k8s节点上直接构建和部署。

```bash
make install
```

## 实现思路

要利用ISTIO重定向pod间的路由，需要以下步骤：

1. 利用label对pod们进行标记
2. 利用ISTIO的DestinationRule，将目标pod标记为某一subset
3. 获取ISTIO的VService，对目标pod所属的Service路由规则进行改写
	1. 增加/查找条目，sourceLabels和endPoint(http.uri.exact/prefix)匹配的情况
	2. 修改destination.subset

因此，一条路由规则是一个元组: `(Namespace:str,DesService:str,SrcPods:PodId[],DesPods:PodId[],EndpointControl[])`

为了方便起见，我们给它起一个友好的名字，扩展元组为 
`(Namespace:str,DesService:str,FriendlyName:str,SrcPods:PodId[],DesPods:PodId[],EndpointControl[])`，
记作`record RouteRule`
然后，我们**规定**`(Namespace:str,DesService:str,FriendlyName:str)`可以唯一确定这样的一个元组，记作`record RouteRuleId`。

从而实现以下功能：

- 传入精确的`DesService`，`Namespace`，可以模糊匹配/不传入`FriendlyName`，查询所有匹配条件的规则
- 传入完整的`record RouteRule`，实现路由规则的增加/修改
- 传入精确完整的`record RouteRuleId`，实现路由规则的删除
- 传入精确的`DesService`，`Namespace`，模糊匹配/不传入`FriendlyName`，实现批量删除

局限性：

1. 假定用户不会手动修改ISTIO资源
2. 假定这些路由规则间不存在冲突，可以正常工作



## 目录结构

```text
├─📂IstioEntities ISTIO资源实体模型定义，一般无需修改，请[参阅官方文档](https://istio.io/latest/zh/docs/reference/config/networking/virtual-service/)
├─📂RouteControlling Bean和Service
│	├─🗒️IRouteController.cs 包含路由控制Service接口和一个假实现
│	├─🗒️RouteController.cs 包含路由控制Service的真实现
│	└─🗒️RouteRule.cs Bean
├─📂Properties
│   └─🗒️launchSettings.json 启动配置│
├─📂TestData 接口测试数据json
│
├─🗒️application.yaml 主要配置文件
├─🗒️appsettings.json 基本配置文件
├─🗒️appsettings.Development.json 基本配置文件(dev)
├─🗒️Dockerfile (自动生成)
├─🗒️MResponse.cs MResponse
├─🗒️Program.cs 程序入口（最小API），包含**请求处理、Bean定义和依赖注入**
├─🗒️README.md 本文件
├─🗒️RouteControlService.csproj 项目文件(自动生成)，包含**依赖项**
├─🗒️RouteControlService.sln 解决方案文件(自动生成)
├─🗒️deploy.yaml 部署配置文件
└─🗒️Makefile 构建脚本
```