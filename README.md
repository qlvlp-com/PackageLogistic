# PackageLogistic

本mod旨在减少游戏内传送带和分拣器使用量，提高游戏帧率，但在一定程度上减少了游戏性，建议通关一次后再使用本mod。

## 版本
version 1.0.0

## 快捷键
ctrl+L

## 配置文件
默认路径：BepInEx/config/com.qlvlp.dsp.PackageLogistic

## 功能
1. 自动从背包中将生产原料投放至生产设备，自动从生产设备回收产品。
2. 自动喷涂，自动使用物流背包里的增产剂对物流背包内的其他产品进行喷涂，可设置是否消耗增产剂。
3. 无限矿物，通过物流背包获取无限矿物。
4. 无限建筑，通过物流背包获取无限建筑物。
5. 无限物品，通过物流背包获取无限物品（无法获取成就）。
6. 无限沙土，沙土数量固定为最大值1G。

## 备注：
为了防止氢和原油溢出，导致原油分解阻塞
1. 氢和原油自动存放只允许储存至60%，手动存放不限制。
2. 火力发电厂使用燃料顺序：精炼油和氢超60%时，谁多使用谁，否则使用煤。

## 安装
### 依赖
本mod依赖BepInEx 5。
### 安装
复制PackageLogistic.dll文件至BepInEx/plugins目录下即可。

## 项目主页
### github
https://github.com/qlvlp-com/PackageLogistic
### 百度网盘
https://pan.baidu.com/s/1GoVky8FkgsrVaxNZJ3E2Mg?pwd=g3sp