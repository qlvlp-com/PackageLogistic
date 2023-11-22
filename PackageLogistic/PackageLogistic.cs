﻿using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;

using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;


namespace PackageLogistic
{

    struct TransportStore
    {
        public int planetIndex;
        public int transportIndex;
        public int storageIndex;

        public TransportStore(int planetIndex, int transportIndex, int storageIndex) : this()
        {
            this.planetIndex = planetIndex;
            this.transportIndex = transportIndex;
            this.storageIndex = storageIndex;
        }
    }

    [BepInPlugin(GUID, NAME, VERSION)]
    public class PackageLogistic : BaseUnityPlugin
    {
        public const string GUID = "com.qlvlp.dsp.PackageLogistic";
        public const string NAME = "PackageLogistic";
        public const string VERSION = "1.0.3";

        ConfigEntry<Boolean> autoSpray;
        ConfigEntry<Boolean> costProliferator;
        ConfigEntry<Boolean> infVeins;
        ConfigEntry<Boolean> infItems;
        ConfigEntry<Boolean> infSand;
        ConfigEntry<Boolean> infBuildings;
        ConfigEntry<Boolean> useStorege;
        ConfigEntry<KeyboardShortcut> hotKey;
        ConfigEntry<Boolean> enableMod;

        DeliveryPackage deliveryPackage;
        Dictionary<int, int> packageItemIndex = new Dictionary<int, int>(); //<itemId,deliveryPackage.grids Index>
        Dictionary<int, List<TransportStore>> transportItemIndex = new Dictionary<int, List<TransportStore>>(); //<itemId, transportStore>
        Dictionary<int, int> incPool = new Dictionary<int, int>()
        {
            {1141, 0 },
            {1142, 0 },
            {1143, 0 }
        };
        Dictionary<string, bool> taskState = new Dictionary<string, bool>();
        int stackSize = 0;
        private const float hydrogenThreshold = 0.6f;
        private const int hydrogenId = 1120;

        private bool showGUI = false;
        private Rect windowRect = new Rect(700, 250, 500, 400);
        private Texture2D windowTexture = new Texture2D(10, 10);

        void Start()
        {
            enableMod = Config.Bind<Boolean>("配置", "EnableMod", true, "启用MOD");
            autoSpray = Config.Bind<Boolean>("配置", "AutoSpray", true, "自动喷涂。自动使用物流背包里的增产剂对物流背包内的其它物品进行喷涂");
            costProliferator = Config.Bind<Boolean>("配置", "CostProliferator", true, "消耗增产剂。自动喷涂时消耗背包里的增产剂");
            infItems = Config.Bind<Boolean>("配置", "InfItems", false, "无限物品。物流背包内所有物品无限数量（无法获取成就）");
            infVeins = Config.Bind<Boolean>("配置", "InfVeins", false, "无限矿物。物流背包内所有矿物无限数量");
            infBuildings = Config.Bind<Boolean>("配置", "InfBuildings", false, "无限建筑。物流背包内所有建筑无限数量");
            infSand = Config.Bind<Boolean>("配置", "InfSand", false, "无限沙土。沙土无限数量（固定为1G）");
            useStorege = Config.Bind<Boolean>("配置", "useStorege", true, "从储物箱和储液罐回收物品");
            hotKey = Config.Bind("窗口快捷键", "Key", new KeyboardShortcut(KeyCode.L, KeyCode.LeftControl));


            windowTexture.SetPixels(Enumerable.Repeat(new Color(0, 0, 0, 1), 100).ToArray());
            windowTexture.Apply();
            new Thread(() =>
            {
                Logger.LogInfo("PackageLogistic start!");
                while (true)
                {
                    DateTime startTime = DateTime.Now;
                    try
                    {
                        if (GameMain.instance == null || GameMain.instance.isMenuDemo || GameMain.isPaused || !GameMain.isRunning || GameMain.data == null)
                        {
                            Logger.LogInfo("Game is not running!");
                            continue;
                        }

                        if (enableMod.Value)
                        {
                            if (infSand.Value && GameMain.mainPlayer.sandCount != 1000000000)
                            {
                                Traverse.Create(GameMain.mainPlayer).Property("sandCount").SetValue(1000000000);
                            }

                            deliveryPackage = GameMain.mainPlayer.deliveryPackage;
                            CreateDeliveryPackageItemIndex();
                            CreateTransportItemIndex();
                            CheckTech();
                            taskState["ProcessTransport"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessTransport, taskState);
                            if (useStorege.Value)
                            {
                                taskState["ProcessStorage"] = false;
                                ThreadPool.QueueUserWorkItem(ProcessStorage, taskState);
                            }
                            else
                            {
                                taskState["ProcessStorage"] = true;
                            }
                            taskState["ProcessAssembler"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessAssembler, taskState);
                            taskState["ProcessMiner"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessMiner, taskState);
                            taskState["ProcessPowerGenerator"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessPowerGenerator, taskState);
                            taskState["ProcessPowerExchanger"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessPowerExchanger, taskState);
                            taskState["ProcessSilo"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessSilo, taskState);
                            taskState["ProcessEjector"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessEjector, taskState);
                            taskState["ProcessLab"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessLab, taskState);
                            var keys = new List<string>(taskState.Keys);
                            while (true)
                            {
                                bool finish = true;
                                DateTime now = DateTime.Now;
                                for (int i = 0; i < keys.Count; i++)
                                {
                                    finish &= taskState[keys[i]];
                                }
                                if (finish)
                                    break;
                                else if ((now - startTime).TotalMilliseconds >= 1000)
                                {
                                    Logger.LogInfo("task state set exception!");
                                    break;
                                }
                                else
                                    Thread.Sleep(5);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogInfo("PackageLogistic exception!");
                        Logger.LogInfo(ex.ToString());
                    }
                    finally
                    {
                        DateTime endTime = DateTime.Now;
                        double cost = (endTime - startTime).TotalMilliseconds;

                        Logger.LogDebug(string.Format("loop cost:{0}", cost));
                        if (cost < 50)
                            Thread.Sleep((int)(50 - cost));
                    }
                }
            }).Start();
        }

        void Update()
        {
            if (hotKey.Value.IsDown())
            {
                showGUI = !showGUI;
            }
        }


        void OnGUI()
        {
            if (showGUI)
            {
                GUI.DrawTexture(windowRect, windowTexture);
                windowRect = GUI.Window(0, windowRect, WindowFunction, "PackageLogistic 设置");
            }
        }

        void WindowFunction(int windowID)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(20);
            enableMod.Value = GUILayout.Toggle(enableMod.Value, "启用MOD");
            GUILayout.Label("启用或停止MOD运行");

            GUILayout.BeginHorizontal();
            autoSpray.Value = GUILayout.Toggle(autoSpray.Value, "自动喷涂");
            costProliferator.Value = GUILayout.Toggle(costProliferator.Value, "消耗增产剂");
            GUILayout.EndHorizontal();
            GUILayout.Label("自动使用物流背包里的增产剂对物流背包内的其它物品进行喷涂");


            infBuildings.Value = GUILayout.Toggle(infBuildings.Value, "无限建筑");
            GUILayout.Label("物流背包内所有建筑无限数量");

            infVeins.Value = GUILayout.Toggle(infVeins.Value, "无限矿物");
            GUILayout.Label("物流背包内所有矿物无限数量");

            infSand.Value = GUILayout.Toggle(infSand.Value, "无限沙土");
            GUILayout.Label("沙土无限数量（固定为1G）");

            infItems.Value = GUILayout.Toggle(infItems.Value, "无限物品");
            GUILayout.Label("物流背包内所有物品无限数量（无法获取成就）");

            useStorege.Value = GUILayout.Toggle(useStorege.Value, "从储物箱和储液罐回收物品");

            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        void CheckTech()
        {
            Logger.LogDebug("CheckTech");
            if (GameMain.history.TechUnlocked(2304) && deliveryPackage.colCount < 5)
            {
                deliveryPackage.colCount = 5;
                deliveryPackage.NotifySizeChange();
            }
            else if (GameMain.history.TechUnlocked(1608) && deliveryPackage.colCount < 4)
            {
                deliveryPackage.colCount = 4;
                deliveryPackage.NotifySizeChange();
            }
            else if (!GameMain.history.TechUnlocked(1608) && deliveryPackage.colCount < 3 || !deliveryPackage.unlocked)
            {
                deliveryPackage.colCount = 3;
                if (!deliveryPackage.unlocked)
                    deliveryPackage.unlocked = true;
                deliveryPackage.NotifySizeChange();
            }

            if (GameMain.history.TechUnlocked(2306))
            {
                stackSize = 5000;
                if (GameMain.mainPlayer.package.size < 160)
                    GameMain.mainPlayer.package.SetSize(160);

            }
            else if (GameMain.history.TechUnlocked(2305))
            {
                stackSize = 4000;
                if (GameMain.mainPlayer.package.size < 140)
                    GameMain.mainPlayer.package.SetSize(140);
            }
            else if (GameMain.history.TechUnlocked(2304))
            {
                stackSize = 3000;
                if (GameMain.mainPlayer.package.size < 120)
                    GameMain.mainPlayer.package.SetSize(120);
            }
            else if (GameMain.history.TechUnlocked(2303))
            {
                stackSize = 2000;
                if (GameMain.mainPlayer.package.size < 110)
                    GameMain.mainPlayer.package.SetSize(110);
            }
            else if (GameMain.history.TechUnlocked(2302))
            {
                stackSize = 1000;
                if (GameMain.mainPlayer.package.size < 100)
                    GameMain.mainPlayer.package.SetSize(100);
            }
            else if (GameMain.history.TechUnlocked(2301))
            {
                stackSize = 500;
                if (GameMain.mainPlayer.package.size < 90)
                    GameMain.mainPlayer.package.SetSize(90);
            }
            else
            {
                stackSize = 300;
                if (GameMain.mainPlayer.package.size < 80)
                    GameMain.mainPlayer.package.SetSize(80);
            }

            if (GameMain.history.TechUnlocked(3510))
            {
                GameMain.history.remoteStationExtraStorage = 40000;
            }
            else if (GameMain.history.TechUnlocked(3509))
            {
                GameMain.history.remoteStationExtraStorage = 15000;
            }
        }


        void CreateDeliveryPackageItemIndex()
        {
            Logger.LogDebug("CreateDeliveryPackageItemIndex");
            for (int index = 0; index < deliveryPackage.gridLength; index++)
            {
                DeliveryPackage.GRID grid = deliveryPackage.grids[index];
                int max_count = Math.Min(grid.recycleCount, grid.stackSizeModified);
                if (grid.itemId > 0)
                {
                    if (!packageItemIndex.ContainsKey(grid.itemId))
                        packageItemIndex.Add(grid.itemId, index);
                    else
                        packageItemIndex[grid.itemId] = index;


                    ItemProto item = LDB.items.Select(grid.itemId);
                    if (!item.CanBuild && stackSize > item.StackSize)
                    {
                        deliveryPackage.grids[index].stackSize = stackSize;
                    }


                    if (infItems.Value)  // 无限物品模式
                    {
                        deliveryPackage.grids[index].count = max_count;
                    }
                    else
                    {
                        if (infVeins.Value && IsVein(grid.itemId))  //无限矿物模式
                        {
                            // 在无限矿物模式下，为防止氢溢出导致原油裂解反应阻塞，氢储量百分比设置为blockThreshold
                            if (grid.itemId == hydrogenId)
                            {
                                max_count = (int)(max_count * hydrogenThreshold);
                            }
                            deliveryPackage.grids[index].count = max_count;
                        }
                        if (infBuildings.Value && item.CanBuild)  //无限建筑模式
                        {
                            deliveryPackage.grids[index].count = max_count;
                        }
                    }

                    SprayDeliveryPackageItem(index);
                }
            }
        }

        void CreateTransportItemIndex()
        {
            Logger.LogDebug("CreateTransportItemIndex");
            transportItemIndex = new Dictionary<int, List<TransportStore>>();
            for (int planetIndex = GameMain.data.factories.Length - 1; planetIndex >= 0; planetIndex--)
            {
                PlanetFactory pf = GameMain.data.factories[planetIndex];
                if (pf == null) continue;
                for (int transportIndex = pf.transport.stationPool.Length - 1; transportIndex >= 0; transportIndex--)
                {
                    StationComponent sc = pf.transport.stationPool[transportIndex];
                    if (sc == null || sc.id <= 0) { continue; }
                    if (sc.isStellar && sc.isCollector == false && sc.isVeinCollector == false)  //星际运输站
                    {
                        for (int storageIndex = sc.storage.Length - 1; storageIndex >= 0; storageIndex--)
                        {
                            StationStore ss = sc.storage[storageIndex];
                            if (ss.itemId <= 0) continue;
                            if (!transportItemIndex.ContainsKey(ss.itemId))
                            {
                                transportItemIndex[ss.itemId] = new List<TransportStore>();
                            }
                            TransportStore store = new TransportStore(planetIndex, transportIndex, storageIndex);
                            transportItemIndex[ss.itemId].Add(new TransportStore(planetIndex, transportIndex, storageIndex));

                            ItemProto item = LDB.items.Select(ss.itemId);  //建筑物储量最大值设置为物品默认堆叠值
                            if (item.CanBuild)
                            {
                                sc.storage[storageIndex].max = Math.Min(item.StackSize * 10, ss.max);
                            }


                            if (infItems.Value)  // 无限物品模式
                            {
                                sc.storage[storageIndex].count = ss.max;
                            }
                            else
                            {
                                if (infVeins.Value && IsVein(ss.itemId))  //无限矿物模式
                                {
                                    // 在无限矿物模式下，为防止氢溢出导致原油裂解反应阻塞，氢储量百分比设置为blockThreshold
                                    if (ss.itemId == hydrogenId)
                                    {
                                        sc.storage[storageIndex].count = (int)(ss.max * hydrogenThreshold);
                                    }
                                    else
                                    {
                                        sc.storage[storageIndex].count = ss.max;
                                    }
                                }

                                if (infBuildings.Value && item.CanBuild)  //无限建筑模式
                                {
                                    sc.storage[storageIndex].count = ss.max;
                                }
                            }

                            SprayTransportItem(store);
                        }
                    }
                }
            }
        }

        bool IsVein(int itemId)
        {
            int[] items = new int[4] { 1000, 1116, 1120, 1121 };  // 水，硫酸，氢，重氢
            if (items.Contains(itemId) || LDB.veins.GetVeinTypeByItemId(itemId) != EVeinType.None)
            {
                return true;
            }
            else
            {
                return false;
            }
        }


        /// <summary>
        /// 对物流背包内物品（建筑物除外）进行增产剂喷涂，优先使用高阶增产剂。
        /// </summary>
        /// <param name="index"></param>
        void SprayDeliveryPackageItem(int index)
        {
            if (!autoSpray.Value)
                return;
            DeliveryPackage.GRID grid = deliveryPackage.grids[index];
            if (grid.itemId <= 0 || grid.count <= 0)
                return;
            if (grid.itemId == 1141 || grid.itemId == 1142 || grid.itemId == 1143)
            {
                deliveryPackage.grids[index].inc = deliveryPackage.grids[index].count * 4;
                return;
            }
            if (!costProliferator.Value && grid.inc < grid.count * 4)
            {
                deliveryPackage.grids[index].inc = grid.count * 4;
                return;
            }
            ItemProto item = LDB.items.Select(grid.itemId);
            if (item.CanBuild)
                return;

            List<(int, int)> proliferators = new List<(int, int)>();
            proliferators.Add((1143, 4));  //增产剂MK.III
            proliferators.Add((1142, 2));  //增产剂MK.II
            proliferators.Add((1141, 1));  //增产剂MK.I
            foreach (var proliferator in proliferators)
            {
                int expectInc = grid.count * proliferator.Item2 - grid.inc;
                if (expectInc <= 0)
                    break;
                int realInc = GetInc(proliferator.Item1, expectInc);
                if (realInc > 0)
                {
                    deliveryPackage.grids[index].inc += realInc;
                }
            }
        }

        /// <summary>
        /// 对星际物流塔内物品（建筑物除外）进行增产剂喷涂，优先使用高阶增产剂。
        /// </summary>
        /// <param name="store"></param>
        void SprayTransportItem(TransportStore store)
        {
            if (!autoSpray.Value)
                return;
            PlanetFactory pf = GameMain.data.factories[store.planetIndex];
            StationComponent sc = pf.transport.stationPool[store.transportIndex];
            StationStore ss = sc.storage[store.storageIndex];
            if (ss.itemId <= 0 || ss.count <= 0)
                return;
            if (ss.itemId == 1141 || ss.itemId == 1142 || ss.itemId == 1143)
            {
                sc.storage[store.storageIndex].inc = ss.count * 4;
                return;
            }
            if (!costProliferator.Value && ss.inc < ss.count * 4)
            {
                sc.storage[store.storageIndex].inc = ss.count * 4;
                return;
            }
            ItemProto item = LDB.items.Select(ss.itemId);
            if (item.CanBuild)
                return;

            List<(int, int)> proliferators = new List<(int, int)>();
            proliferators.Add((1143, 4));  //增产剂MK.III
            proliferators.Add((1142, 2));  //增产剂MK.II
            proliferators.Add((1141, 1));  //增产剂MK.I
            foreach (var proliferator in proliferators)
            {
                int expectInc = ss.count * proliferator.Item2 - ss.inc;
                if (expectInc <= 0)
                    break;
                int realInc = GetInc(proliferator.Item1, expectInc);
                if (realInc > 0)
                {
                    sc.storage[store.storageIndex].inc += realInc;
                }
            }
        }


        //从增产点数池中获取指定增产剂类型的增产点数
        int GetInc(int proliferatorId, int count)
        {
            int realCount = 0;
            int factor;
            if (proliferatorId == 1143)
                factor = 75;
            else if (proliferatorId == 1142)
                factor = 30;
            else if (proliferatorId == 1141)
                factor = 15;
            else
                return 0;
            if (count <= 0)
                return 0;

            while (true)
            {
                if (incPool[proliferatorId] >= count)
                {
                    incPool[proliferatorId] -= count;
                    realCount += count;
                    return realCount;
                }
                else
                {
                    realCount += incPool[proliferatorId];
                    count -= incPool[proliferatorId];
                    incPool[proliferatorId] = 0;
                    int[] result = TakeItem(proliferatorId, 600 / factor);
                    if (result[0] == 0)
                        return realCount;
                    else
                    {
                        incPool[proliferatorId] = result[0] * factor;
                    }
                }
            }
        }


        //行星内物流运输站
        void ProcessTransport(object state)
        {
            Logger.LogDebug("ProcessTransport");
            for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
            {
                PlanetFactory pf = GameMain.data.factories[index];
                if (pf == null) continue;
                foreach (StationComponent sc in pf.transport.stationPool)
                {
                    if (sc == null || sc.id <= 0) { continue; }
                    if (sc.isStellar == false && sc.isCollector == false && sc.isVeinCollector == false) //行星运输站
                    {
                        for (int i = sc.storage.Length - 1; i >= 0; i--)
                        {
                            StationStore ss = sc.storage[i];
                            if (ss.itemId <= 0 || !packageItemIndex.ContainsKey(ss.itemId)) continue;
                            if (ss.localLogic == ELogisticStorage.Supply && ss.count > 0)
                            {
                                int[] result;
                                result = AddItem(ss.itemId, ss.count, ss.inc, false);
                                sc.storage[i].count -= result[0];
                                sc.storage[i].inc -= result[1];
                            }
                            else if (ss.localLogic == ELogisticStorage.Demand)
                            {
                                int expectCount = ss.max - ss.localOrder - ss.remoteOrder - ss.count;
                                if (expectCount <= 0) continue;
                                int[] result = TakeItem(ss.itemId, expectCount);
                                sc.storage[i].count += result[0];
                                sc.storage[i].inc += result[1];
                            }
                        }
                    }

                }
            }
            taskState["ProcessTransport"] = true;
        }


        //仓储设备（储物箱，储液罐）
        void ProcessStorage(object state)
        {
            Logger.LogDebug("ProcessStorage");
            for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
            {

                PlanetFactory pf = GameMain.data.factories[index];
                if (pf == null) continue;
                foreach (StorageComponent sc in pf.factoryStorage.storagePool)
                {
                    if (sc == null || sc.isEmpty) continue;
                    for (int i = sc.grids.Length - 1; i >= 0; i--)
                    {
                        StorageComponent.GRID grid = sc.grids[i];
                        if (grid.itemId <= 0 || grid.count <= 0 || !packageItemIndex.ContainsKey(grid.itemId)) continue;
                        int[] result = AddItem(grid.itemId, grid.count, grid.inc, false);
                        if (result[0] != 0)
                        {
                            sc.grids[i].count -= result[0];
                            sc.grids[i].inc -= result[1];
                        }
                    }
                    sc.Sort();
                }

                for (int i = pf.factoryStorage.tankPool.Length - 1; i >= 0; --i)
                {
                    TankComponent tc = pf.factoryStorage.tankPool[i];
                    if (tc.id == 0 || tc.fluidId == 0 || tc.fluidCount == 0 || !packageItemIndex.ContainsKey(tc.fluidId)) continue;
                    int[] result = AddItem(tc.fluidId, tc.fluidCount, tc.fluidInc, false);
                    pf.factoryStorage.tankPool[i].fluidCount -= result[0];
                    pf.factoryStorage.tankPool[i].fluidInc -= result[1];
                    if (pf.factoryStorage.tankPool[i].fluidCount <= 0)
                    {
                        pf.factoryStorage.tankPool[i].fluidId = 0;
                        pf.factoryStorage.tankPool[i].fluidInc = 0;
                    }
                }

            }
            taskState["ProcessStorage"] = true;
        }


        //熔炉、制造台、原油精炼厂、化工厂、粒子对撞机
        void ProcessAssembler(object state)
        {
            Logger.LogDebug("ProcessAssembler");
            for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
            {
                PlanetFactory pf = GameMain.data.factories[index];
                if (pf == null) continue;
                foreach (AssemblerComponent ac in pf.factorySystem.assemblerPool)
                {
                    if (ac.id <= 0 || ac.recipeId <= 0) continue;
                    for (int i = ac.products.Length - 1; i >= 0; i--)
                    {
                        if (ac.produced[i] > 0)
                            ac.produced[i] -= AddItem(ac.products[i], ac.produced[i], 0)[0];
                    }
                    for (int i = ac.requires.Length - 1; i >= 0; i--)
                    {
                        int expectCount = Math.Max(ac.requireCounts[i] * 5 - ac.served[i], 0);
                        if (expectCount > 0)
                        {
                            int[] result = TakeItem(ac.requires[i], expectCount);
                            ac.served[i] += result[0];
                            ac.incServed[i] += result[1];
                        }
                    }
                }
            }
            taskState["ProcessAssembler"] = true;
        }


        // 采矿机、大型采矿机、抽水站、原油萃取站、轨道采集器
        void ProcessMiner(object state)
        {
            Logger.LogDebug("ProcessMiner");
            for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
            {
                PlanetFactory pf = GameMain.data.factories[index];
                if (pf == null) continue;

                for (int i = pf.factorySystem.minerPool.Length - 1; i >= 0; i--)
                {
                    MinerComponent mc = pf.factorySystem.minerPool[i];
                    if (mc.id <= 0 || mc.productId <= 0 || mc.productCount <= 0) continue;
                    int[] result = AddItem(mc.productId, mc.productCount, 0, false);
                    pf.factorySystem.minerPool[i].productCount -= result[0];
                }

                //大型矿机，轨道采集器
                foreach (StationComponent sc in pf.transport.stationPool)
                {
                    if (sc == null || sc.id <= 0) { continue; }
                    if (sc.isStellar && sc.isCollector)  //轨道采集器
                    {
                        for (int i = sc.storage.Length - 1; i >= 0; i--)
                        {
                            StationStore ss = sc.storage[i];
                            if (ss.itemId <= 0 || ss.count <= 0 || !packageItemIndex.ContainsKey(ss.itemId) || ss.remoteLogic != ELogisticStorage.Supply)
                                continue;

                            int[] result;
                            result = AddItem(ss.itemId, ss.count, 0, false);

                            sc.storage[i].count -= result[0];
                        }
                    }
                    else if (sc.isVeinCollector)  // 大型采矿机
                    {
                        StationStore ss = sc.storage[0];
                        if (ss.itemId <= 0 || ss.count <= 0 || !packageItemIndex.ContainsKey(ss.itemId) || ss.localLogic != ELogisticStorage.Supply)
                            continue;

                        int[] result = AddItem(ss.itemId, ss.count, 0, false);
                        sc.storage[0].count -= result[0];
                    }
                }
            }

            taskState["ProcessMiner"] = true;
        }

        //火力发电厂（煤）、核聚变电站、人造恒星、射线接受器
        void ProcessPowerGenerator(object state)
        {
            Logger.LogDebug("ProcessPowerGenerator");
            for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
            {
                PlanetFactory pf = GameMain.data.factories[index];
                if (pf == null) continue;
                for (int i = pf.powerSystem.genPool.Length - 1; i >= 0; i--)
                {
                    PowerGeneratorComponent pgc = pf.powerSystem.genPool[i];
                    if (pgc.id <= 0) continue;
                    if (pgc.gamma == true) // 射线接受器
                    {
                        if (pgc.catalystPoint + pgc.catalystIncPoint < 3600)
                        {
                            int[] result = TakeItem(1209, 3);
                            if (result[0] > 0)
                            {
                                pf.powerSystem.genPool[i].catalystId = 1209;
                                pf.powerSystem.genPool[i].catalystPoint += result[0] * 3600;
                                pf.powerSystem.genPool[i].catalystIncPoint += result[1] * 3600;
                            }
                        }
                        if (pgc.productId > 0 && pgc.productCount >= 1)
                        {
                            int[] result = AddItem(pgc.productId, (int)pgc.productCount, 0);
                            pf.powerSystem.genPool[i].productCount -= result[0];
                        }
                        continue;
                    }

                    int fuelId = 0;
                    switch (pgc.fuelMask)
                    {
                        case 1: //火力发电厂使用燃料顺序：精炼油和氢超60%时，谁多使用谁，否则使用煤
                            float p_1114 = 0.0f;
                            float p_1120 = 0.0f;
                            if (packageItemIndex.ContainsKey(1114))
                            {
                                var grid = deliveryPackage.grids[packageItemIndex[1114]];
                                p_1114 = (float)grid.count / (float)(grid.stackSizeModified);
                            }
                            if (packageItemIndex.ContainsKey(1120))
                            {
                                var grid = deliveryPackage.grids[packageItemIndex[1120]];
                                p_1120 = (float)grid.count / (float)grid.stackSizeModified;
                            }
                            if (p_1114 >= p_1120 && p_1114 > 0.60)
                            {
                                fuelId = 1114; // 精炼油
                            }
                            else if (p_1120 >= p_1114 && p_1120 > 0.60)
                            {
                                fuelId = 1120; // 氢
                            }
                            else
                            {
                                fuelId = 1006; //煤
                            }
                            break;
                        case 2: fuelId = 1802; break;
                        case 4: fuelId = 1803; break;
                    }
                    if (fuelId != pgc.fuelId && pgc.fuelCount == 0)
                    {
                        int[] result = TakeItem(fuelId, 5);
                        pf.powerSystem.genPool[i].SetNewFuel(fuelId, (short)result[0], (short)result[1]);
                    }
                    else if (fuelId == pgc.fuelId && pgc.fuelCount < 5)
                    {
                        int[] result = TakeItem(fuelId, 5 - pgc.fuelCount);
                        pf.powerSystem.genPool[i].fuelCount += (short)result[0];
                        pf.powerSystem.genPool[i].fuelInc += (short)result[1];
                    }
                }
            }
            taskState["ProcessPowerGenerator"] = true;
        }

        //能量枢纽
        void ProcessPowerExchanger(object state)
        {
            Logger.LogDebug("ProcessPowerExchanger");
            for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
            {
                PlanetFactory pf = GameMain.data.factories[index];
                if (pf == null) continue;
                for (int i = pf.powerSystem.excPool.Length - 1; i >= 0; i--)
                {
                    PowerExchangerComponent pec = pf.powerSystem.excPool[i];
                    if (pec.targetState == -1) //放电
                    {
                        if (pec.fullCount < 3)
                        {
                            int[] result = TakeItem(pec.fullId, 3 - pec.fullCount);
                            pf.powerSystem.excPool[i].fullCount += (short)result[0];
                        }
                        if (pec.emptyCount > 0)
                        {
                            int[] result = AddItem(pec.emptyId, pec.emptyCount, 0);
                            pf.powerSystem.excPool[i].emptyCount -= (short)result[0];
                        }
                    }
                    else if (pec.targetState == 1) //充电
                    {
                        if (pec.emptyCount < 5)
                        {
                            int[] result = TakeItem(pec.emptyId, 5 - pec.emptyCount);
                            pf.powerSystem.excPool[i].emptyCount += (short)result[0];
                        }
                        if (pec.fullCount > 0)
                        {
                            int[] result = AddItem(pec.fullId, pec.fullCount, 0);
                            pf.powerSystem.excPool[i].fullCount -= (short)result[0];
                        }
                    }
                }
            }
            taskState["ProcessPowerExchanger"] = true;
        }

        //火箭发射井
        void ProcessSilo(object state)
        {
            Logger.LogDebug("ProcessSilo");
            for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
            {
                PlanetFactory pf = GameMain.data.factories[index];
                if (pf == null) continue;
                for (int i = pf.factorySystem.siloPool.Length - 1; i >= 0; i--)
                {
                    SiloComponent sc = pf.factorySystem.siloPool[i];
                    if (sc.id > 0 && sc.bulletCount <= 3)
                    {
                        int[] result = TakeItem(sc.bulletId, 10);
                        pf.factorySystem.siloPool[i].bulletCount += result[0];
                        pf.factorySystem.siloPool[i].bulletInc += result[1];
                    }
                }
            }
            taskState["ProcessSilo"] = true;
        }

        //电磁弹射器
        void ProcessEjector(object state)
        {
            Logger.LogDebug("ProcessEjector");
            for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
            {
                PlanetFactory pf = GameMain.data.factories[index];
                if (pf == null) continue;
                for (int i = pf.factorySystem.ejectorPool.Length - 1; i >= 0; i--)
                {
                    EjectorComponent ec = pf.factorySystem.ejectorPool[i];
                    if (ec.id > 0 && ec.bulletCount <= 5)
                    {
                        int[] result = TakeItem(ec.bulletId, 15);
                        pf.factorySystem.ejectorPool[i].bulletCount += result[0];
                        pf.factorySystem.ejectorPool[i].bulletInc += result[1];
                    }
                }
            }
            taskState["ProcessEjector"] = true;
        }

        //研究站
        void ProcessLab(object state)
        {
            Logger.LogDebug("ProcessLab");
            for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
            {
                PlanetFactory pf = GameMain.data.factories[index];
                if (pf == null) continue;
                foreach (LabComponent lc in pf.factorySystem.labPool)
                {
                    if (lc.id <= 0) continue;
                    if (lc.recipeId > 0)
                    {
                        for (int i = lc.products.Length - 1; i >= 0; i--)
                        {
                            if (lc.produced[i] > 0)
                            {
                                int[] result = AddItem(lc.products[i], lc.produced[i], 0);
                                lc.produced[i] -= result[0];
                            }
                        }
                        for (int i = lc.requires.Length - 1; i >= 0; i--)
                        {
                            int expectCount = lc.requireCounts[i] * 3 - lc.served[i] - lc.incServed[i];
                            int[] result = TakeItem(lc.requires[i], expectCount);
                            lc.served[i] += result[0];
                            lc.incServed[i] += result[1];
                        }
                    }
                    else if (lc.researchMode == true)
                    {
                        for (int i = lc.matrixPoints.Length - 1; i >= 0; i--)
                        {
                            if (lc.matrixPoints[i] <= 0) continue;
                            if (lc.matrixServed[i] >= lc.matrixPoints[i] * 3600) continue;
                            int[] result = TakeItem(LabComponent.matrixIds[i], lc.matrixPoints[i]);
                            lc.matrixServed[i] += result[0] * 3600;
                            lc.matrixIncServed[i] += result[1] * 3600;
                        }
                    }
                }
            }
            taskState["ProcessLab"] = true;
        }


        // 向背包内放入氢气，为防止氢气溢出阻塞原油裂解反应，氢气储量百分比最大为hydrogenThreshold
        //int[] AddHydrogen(int itemId, int count, int inc)
        //{
        //    if (itemId <= 0 || count <= 0 || !packageItemIndex.ContainsKey(itemId))
        //        return new int[2] { 0, 0 };
        //    int index = packageItemIndex[itemId];
        //    if (index < 0 || deliveryPackage.grids[index].itemId != itemId)
        //        return new int[2] { 0, 0 };

        //    int max_count = (int)(Math.Min(deliveryPackage.grids[index].recycleCount, deliveryPackage.grids[index].stackSizeModified) * hydrogenThreshold);
        //    int quota = max_count - deliveryPackage.grids[index].count;
        //    if (quota <= 0)
        //    {
        //        return new int[2] { 0, 0 };
        //    }
        //    if (count <= quota)
        //    {
        //        deliveryPackage.grids[index].count += count;
        //        deliveryPackage.grids[index].inc += inc;
        //        SprayDeliveryPackageItem(index);
        //        return new int[2] { count, inc };
        //    }
        //    else
        //    {
        //        deliveryPackage.grids[index].count = max_count;
        //        int realInc = SplitInc(count, inc, quota);
        //        deliveryPackage.grids[index].inc += realInc;
        //        SprayDeliveryPackageItem(index);
        //        return new int[2] { quota, realInc };
        //    }
        //}


        //优先使用物流背包，其次星际物流塔

        //优先使用物流背包，其次星际物流塔
        int[] AddItem(int itemId, int count, int inc, bool assembler = true)
        {
            int[] result = AddDeliveryPackageItem(itemId, count, inc, assembler);
            if (result[0] == count)
            {
                return result;
            }
            int[] result1 = AddTransportItem(itemId, count - result[0], inc - result[1], assembler);
            result1[0] += result[0];
            result1[1] += result[1];
            return result1;
        }


        /// <summary>
        /// 向物流背包中放入物品
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="count"></param>
        /// <param name="inc"></param>
        /// <param name="assembler">物品是否来源于制造设备</param>
        /// <returns>{实际放入物品数量， 实际放入物品增产量}</returns>
        int[] AddDeliveryPackageItem(int itemId, int count, int inc, bool assembler = true)
        {
            if (itemId <= 0 || count <= 0 || !packageItemIndex.ContainsKey(itemId))
                return new int[2] { 0, 0 };
            int index = packageItemIndex[itemId];
            if (index < 0 || deliveryPackage.grids[index].itemId != itemId)
                return new int[2] { 0, 0 };

            int max_count = Math.Min(deliveryPackage.grids[index].recycleCount, deliveryPackage.grids[index].stackSizeModified);
            if (assembler == false && itemId == hydrogenId)  // 当氢气储量超过阈值后，不再接收非制造设备的氢气，以防止阻塞原油裂解反应。
                max_count = (int)(Math.Min(deliveryPackage.grids[index].recycleCount, deliveryPackage.grids[index].stackSizeModified) * hydrogenThreshold);
            int quota = max_count - deliveryPackage.grids[index].count;
            if (quota <= 0)
            {
                return new int[2] { 0, 0 };
            }
            if (count <= quota)
            {
                deliveryPackage.grids[index].count += count;
                deliveryPackage.grids[index].inc += inc;
                SprayDeliveryPackageItem(index);
                return new int[2] { count, inc };
            }
            else
            {
                deliveryPackage.grids[index].count = max_count;
                int realInc = SplitInc(count, inc, quota);
                deliveryPackage.grids[index].inc += realInc;
                SprayDeliveryPackageItem(index);
                return new int[2] { quota, realInc };
            }
        }

        /// <summary>
        /// 向星际物流运输站中放入物品
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="count"></param>
        /// <param name="inc"></param>
        /// <param name="assembler">物品是否来源于制造设备</param>
        /// <returns>{实际放入物品数量， 实际放入物品增产量}</returns>
        int[] AddTransportItem(int itemId, int count, int inc, bool assembler = true)
        {
            if (itemId <= 0 || count <= 0 || !transportItemIndex.ContainsKey(itemId))
                return new int[2] { 0, 0 };
            int[] result = new int[2] { 0, 0 };
            foreach (TransportStore store in transportItemIndex[itemId])
            {
                PlanetFactory pf = GameMain.data.factories[store.planetIndex];
                StationComponent sc = pf.transport.stationPool[store.transportIndex];
                StationStore ss = sc.storage[store.storageIndex];
                if (ss.itemId != itemId)
                    continue;
                int quota = ss.max - ss.localOrder - ss.remoteOrder - ss.count;
                if (assembler == false && itemId == hydrogenId)  // 当氢气储量超过阈值后，不再接收非制造设备的氢气，以防止阻塞原油裂解反应。
                    quota = (int)(ss.max * hydrogenThreshold) - ss.localOrder - ss.remoteOrder - ss.count;
                if (quota <= 0)
                    continue;
                else if (count <= quota)
                {
                    sc.storage[store.storageIndex].count += count;
                    sc.storage[store.storageIndex].inc += inc;
                    result[0] += count;
                    result[1] += inc;
                    count = 0;
                    inc = 0;
                    SprayTransportItem(store);
                    break;
                }
                else
                {
                    sc.storage[store.storageIndex].count += quota;
                    int realInc = SplitInc(count, inc, quota);
                    sc.storage[store.storageIndex].inc += realInc;
                    result[0] += quota;
                    result[1] += realInc;
                    count -= quota;
                    inc -= realInc;
                    SprayTransportItem(store);
                }
            }
            return result;
        }

        //优先使用物流背包，其次星际物流塔
        int[] TakeItem(int itemId, int count)
        {
            int[] result = TakeDeliveryPackageItem(itemId, count);
            if (result[0] == count)
            {
                return result;
            }
            int[] result1 = TakeTransportItem(itemId, count - result[0]);
            result1[0] += result[0];
            result1[1] += result[1];
            return result1;
        }

        /// <summary>
        /// 从物流背包中拿取物品
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="count"></param>
        /// <returns>{实际取出物品数量， 实际取出物品增产量}</returns>
        int[] TakeDeliveryPackageItem(int itemId, int count)
        {
            if (itemId <= 0 || count <= 0 || !packageItemIndex.ContainsKey(itemId))
                return new int[2] { 0, 0 };
            int index = packageItemIndex[itemId];
            if (index < 0 || deliveryPackage.grids[index].itemId != itemId)
                return new int[2] { 0, 0 };
            if (deliveryPackage.grids[index].count <= deliveryPackage.grids[index].requireCount)
                return new int[2] { 0, 0 };
            int quota = deliveryPackage.grids[index].count - deliveryPackage.grids[index].requireCount;
            if (count <= quota)
            {
                int realInc = SplitInc(deliveryPackage.grids[index].count, deliveryPackage.grids[index].inc, count);
                deliveryPackage.grids[index].count -= count;
                deliveryPackage.grids[index].inc -= realInc;
                return new int[2] { count, realInc };
            }
            else
            {
                int realInc = SplitInc(deliveryPackage.grids[index].count, deliveryPackage.grids[index].inc, quota);
                deliveryPackage.grids[index].count -= quota;
                deliveryPackage.grids[index].inc -= realInc;
                return new int[2] { quota, realInc };
            }
        }

        /// <summary>
        /// 从星际物流运输站中拿取物品
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="count"></param>
        /// <returns>{实际取出物品数量， 实际取出物品增产量}</returns>
        int[] TakeTransportItem(int itemId, int count)
        {
            if (itemId <= 0 || count <= 0 || !transportItemIndex.ContainsKey(itemId))
                return new int[2] { 0, 0 };
            int[] result = new int[2] { 0, 0 };
            foreach (TransportStore store in transportItemIndex[itemId])
            {
                PlanetFactory pf = GameMain.data.factories[store.planetIndex];
                StationComponent sc = pf.transport.stationPool[store.transportIndex];
                StationStore ss = sc.storage[store.storageIndex];
                if (ss.itemId != itemId)
                    continue;
                if (ss.count <= 0)
                    continue;
                else if (count <= ss.count)
                {
                    int realInc = SplitInc(ss.count, ss.inc, count);
                    sc.storage[store.storageIndex].count -= count;
                    sc.storage[store.storageIndex].inc -= realInc;
                    result[0] += count;
                    result[1] += realInc;
                    break;
                }
                else
                {
                    count -= ss.count;
                    result[0] += ss.count;
                    result[1] += ss.inc;
                    sc.storage[store.storageIndex].count = 0;
                    sc.storage[store.storageIndex].inc = 0;
                }
            }
            return result;
        }
        int SplitInc(int count, int inc, int expectCount)
        {
            int num1 = inc / count;
            int num2 = inc - num1 * count;
            count -= expectCount;
            int num3 = num2 - count;
            int num4 = num3 > 0 ? num1 * expectCount + num3 : num1 * expectCount;
            inc -= num4;
            return num4;
        }

    }

}
