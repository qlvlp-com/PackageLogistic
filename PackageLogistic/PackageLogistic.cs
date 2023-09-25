using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;

using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PackageLogistic
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class PackageLogistic : BaseUnityPlugin
    {
        public const string GUID = "com.qlvlp.dsp.PackageLogistic";
        public const string NAME = "PackageLogistic";
        public const string VERSION = "1.0.0";

        ConfigEntry<Boolean> autoSpray;
        ConfigEntry<Boolean> costProliferator;
        ConfigEntry<Boolean> infVeins;
        ConfigEntry<Boolean> infItems;
        ConfigEntry<Boolean> infSand;
        ConfigEntry<Boolean> infBuildings;
        ConfigEntry<KeyboardShortcut> hotKey;
        ConfigEntry<Boolean> enableMod;

        DeliveryPackage deliveryPackage;
        Dictionary<int, int> itemIndex = new Dictionary<int, int>(); //<itemId,deliveryPackage.grids Index>
        Dictionary<int, int> incPool = new Dictionary<int, int>()
        {
            {1141, 0 },
            {1142, 0 },
            {1143, 0 }
        };

        Dictionary<string, bool> taskState = new Dictionary<string, bool>();

        private bool showGUI = false;

        private Rect windowRect = new Rect(50, 50, 500, 350);
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

                        if (infSand.Value && GameMain.mainPlayer.sandCount != 1000000000)
                        {
                            Traverse.Create(GameMain.mainPlayer).Property("sandCount").SetValue(1000000000);
                        }

                        if (enableMod.Value)
                        {
                            deliveryPackage = GameMain.mainPlayer.deliveryPackage;
                            ItemIndex();
                            CheckTech();
                            taskState["ProcessTransport"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessTransport, taskState);
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
                windowRect = GUI.Window(0, windowRect, WindowFunction, "设置");
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
                if (GameMain.mainPlayer.package.size < 160)
                    GameMain.mainPlayer.package.SetSize(160);

            }
            if (GameMain.history.TechUnlocked(2305))
            {
                if (GameMain.mainPlayer.package.size < 140)
                    GameMain.mainPlayer.package.SetSize(140);
            }
            if (GameMain.history.TechUnlocked(2304))
            {
                if (GameMain.mainPlayer.package.size < 120)
                    GameMain.mainPlayer.package.SetSize(120);
            }
            if (GameMain.history.TechUnlocked(2303))
            {
                if (GameMain.mainPlayer.package.size < 110)
                    GameMain.mainPlayer.package.SetSize(110);
            }
            if (GameMain.history.TechUnlocked(2302))
            {
                if (GameMain.mainPlayer.package.size < 100)
                    GameMain.mainPlayer.package.SetSize(100);
            }
            if (GameMain.history.TechUnlocked(2301))
            {
                if (GameMain.mainPlayer.package.size < 90)
                    GameMain.mainPlayer.package.SetSize(90);
            }
            else
            {
                if (GameMain.mainPlayer.package.size < 80)
                    GameMain.mainPlayer.package.SetSize(80);
            }

        }

        void AutoSpray(int index)
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

            ItemProto item = LDB.items.Select(grid.itemId);
            if (item.CanBuild)
                return;
            if (!costProliferator.Value && grid.inc < grid.count * 4)
            {
                deliveryPackage.grids[index].inc = grid.count * 4;
                return;
            }

            int index1 = Array.FindIndex(deliveryPackage.grids, g => g.itemId == 1141); //增产剂MK.I
            int index2 = Array.FindIndex(deliveryPackage.grids, g => g.itemId == 1142); //增产剂MK.II
            int index3 = Array.FindIndex(deliveryPackage.grids, g => g.itemId == 1143); //增产剂MK.III
            if (index3 >= 0 && deliveryPackage.grids[index3].count > 0 && grid.inc < grid.count * 4)
            {
                int expectInc = grid.count * 4 - grid.inc;
                int realInc = GetInc(1143, expectInc);
                deliveryPackage.grids[index].inc += realInc;
            }
            else if (index2 >= 0 && deliveryPackage.grids[index2].count > 0 && grid.inc < grid.count * 2)
            {
                int expectInc = grid.count * 2 - grid.inc;
                int realInc = GetInc(1142, expectInc);
                deliveryPackage.grids[index].inc += realInc;
            }
            else if (index1 >= 0 && deliveryPackage.grids[index1].count > 0 && grid.inc < grid.count * 1)
            {
                int expectInc = grid.count * 1 - grid.inc;
                int realInc = GetInc(1141, expectInc);
                deliveryPackage.grids[index].inc += realInc;
            }
        }

        int GetInc(int incId, int count)
        {
            int realCount = 0;
            int factor;
            if (incId == 1143)
                factor = 75;
            else if (incId == 1142)
                factor = 30;
            else if (incId == 1141)
                factor = 15;
            else
                return 0;
            if (count <= 0)
                return 0;

            while (true)
            {
                if (incPool[incId] >= count)
                {
                    incPool[incId] -= count;
                    realCount += count;
                    return realCount;
                }
                else
                {
                    realCount += incPool[incId];
                    count -= incPool[incId];
                    incPool[incId] = 0;
                    int[] result = TakeItem(incId, 600 / factor);
                    if (result[0] == 0)
                        return realCount;
                    else
                    {
                        incPool[incId] = result[0] * factor;
                    }
                }
            }
        }

        void ItemIndex()
        {
            Logger.LogDebug("ItemIndex");
            for (int index = 0; index < deliveryPackage.gridLength; index++)
            {
                DeliveryPackage.GRID grid = deliveryPackage.grids[index];
                int max_count = Math.Min(grid.recycleCount, grid.stackSizeModified);
                if (grid.itemId > 0)
                {
                    if (!itemIndex.ContainsKey(grid.itemId))
                        itemIndex.Add(grid.itemId, index);
                    else
                        itemIndex[grid.itemId] = index;

                    ItemProto item = LDB.items.Select(grid.itemId);
                    if (!item.CanBuild && item.StackSize < 1000)
                        deliveryPackage.grids[index].stackSize = 1000;

                    if (infItems.Value && max_count != grid.count)
                    {
                        deliveryPackage.grids[index].count = max_count;
                    }
                    else
                    {
                        if (infVeins.Value && max_count != grid.count)
                        {
                            int[] items = new int[4] { 1000, 1116, 1120, 1121 };  // 水，硫酸，氢，重氢
                            if (items.Contains(grid.itemId) || LDB.veins.GetVeinTypeByItemId(grid.itemId) != EVeinType.None)
                            {
                                if (grid.itemId == 1120 && grid.count < 0.60 * max_count)  // 为了防止氢和原油溢出，导致原油分解阻塞，氢和原油只允许储存60%
                                {
                                    deliveryPackage.grids[index].count = (int)(0.60 * max_count);
                                }
                                else if (grid.itemId != 1120)
                                {
                                    deliveryPackage.grids[index].count = max_count;
                                }
                            }
                        }
                        if (infBuildings.Value && max_count != grid.count && item.CanBuild)
                        {
                            deliveryPackage.grids[index].count = max_count;
                        }
                    }

                    AutoSpray(index);
                }
            }
        }


        //行星内物流运输站、星际物流运输站
        //当星际运输站本地物流和星际物流同时为供应时，向背包内投放物品，同时为需求时从背包内获取物品
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
                    if(sc.isStellar && sc.isCollector == false && sc.isVeinCollector == false)  //星际运输站
                    {
                        for (int i = sc.storage.Length - 1; i >= 0; i--)
                        {
                            StationStore ss = sc.storage[i];
                            if (ss.itemId <= 0 || !itemIndex.ContainsKey(ss.itemId)) continue;
                            if (ss.localLogic == ELogisticStorage.Supply && ss.remoteLogic == ELogisticStorage.Supply && ss.count > 0)
                            {
                                int[] result = AddItem(ss.itemId, ss.count, ss.inc);
                                sc.storage[i].count -= result[0];
                                sc.storage[i].inc -= result[1];
                            }
                            else if (ss.localLogic == ELogisticStorage.Demand && ss.remoteLogic == ELogisticStorage.Demand)
                            {
                                int expectCount = ss.max - ss.localOrder - ss.remoteOrder - ss.count;
                                if (expectCount <= 0) continue;
                                int[] result = TakeItem(ss.itemId, expectCount);
                                sc.storage[i].count += result[0];
                                sc.storage[i].inc += result[1];
                            }
                        }
                    }
                    if(sc.isStellar == false && sc.isCollector == false && sc.isVeinCollector == false) //行星运输站
                    {
                        for (int i = sc.storage.Length - 1; i >= 0; i--)
                        {
                            StationStore ss = sc.storage[i];
                            if (ss.itemId <= 0 || !itemIndex.ContainsKey(ss.itemId)) continue;
                            if (ss.localLogic == ELogisticStorage.Supply && ss.count > 0)
                            {
                                int[] result = AddItem(ss.itemId, ss.count, ss.inc);
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
                    int[] result = AddItem(mc.productId, mc.productCount, 0);
                    pf.factorySystem.minerPool[i].productCount -= result[0];
                }

                //大型矿机，轨道采集器
                foreach (StationComponent sc in pf.transport.stationPool)
                {
                    if (sc == null || sc.id <= 0) { continue; }
                    if(sc.isStellar && sc.isCollector)  //轨道采集器
                    {
                        for (int i = sc.storage.Length - 1; i >= 0; i--)
                        {
                            StationStore ss = sc.storage[i];
                            if (ss.itemId <= 0 || ss.count <= 0 || !itemIndex.ContainsKey(ss.itemId) || ss.remoteLogic != ELogisticStorage.Supply)
                                continue;

                            int[] result = AddItem(ss.itemId, ss.count, 0);
                            sc.storage[i].count -= result[0];
                        }
                    }
                    else if(sc.isVeinCollector)  // 大型采矿机
                    {
                        StationStore ss = sc.storage[0];
                        if (ss.itemId <= 0 || ss.count <= 0 || !itemIndex.ContainsKey(ss.itemId) || ss.localLogic != ELogisticStorage.Supply)
                            continue;

                        int[] result = AddItem(ss.itemId, ss.count, 0);
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
                            if (itemIndex.ContainsKey(1114))
                            {
                                var grid = deliveryPackage.grids[itemIndex[1114]];
                                p_1114 = (float)grid.count / (float)(grid.stackSizeModified);
                            }
                            if (itemIndex.ContainsKey(1120))
                            {
                                var grid = deliveryPackage.grids[itemIndex[1120]];
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


        /// <summary>
        /// 
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="count"></param>
        /// <param name="inc"></param>
        /// <returns>{实际放入物品数量， 实际放入物品增产量}</returns>
        int[] AddItem(int itemId, int count, int inc)
        {
            if (itemId <= 0 || count <= 0 || !itemIndex.ContainsKey(itemId))
                return new int[2] { 0, 0 };
            int index = itemIndex[itemId];
            if (index < 0 || deliveryPackage.grids[index].itemId != itemId)
                return new int[2] { 0, 0 };

            // 为了防止氢和原油溢出，导致原油分解阻塞，氢和原油只允许储存60%
            int max_count = Math.Min(deliveryPackage.grids[index].recycleCount, deliveryPackage.grids[index].stackSizeModified);
            if (itemId == 1120 || itemId == 1114)
            {
                max_count = (int)(max_count * 0.60);
            }

            int quota = max_count - deliveryPackage.grids[index].count;
            if(quota <= 0)
            {
                return new int[2] { 0, 0 };
            }
            if (count <= quota)
            {
                deliveryPackage.grids[index].count += count;
                deliveryPackage.grids[index].inc += inc;
                AutoSpray(index);
                return new int[2] { count, inc };
            }
            else
            {
                deliveryPackage.grids[index].count = max_count;
                int realInc = SplitInc(count, inc, quota);
                deliveryPackage.grids[index].inc += realInc;
                AutoSpray(index);
                return new int[2] { quota, realInc };
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="count"></param>
        /// <param name="inc"></param>
        /// <returns>{实际取出物品数量， 实际取出物品增产量}</returns>
        int[] TakeItem(int itemId, int count)
        {
            if (itemId <= 0 || count <= 0 || !itemIndex.ContainsKey(itemId))
                return new int[2] { 0, 0 };
            int index = itemIndex[itemId];
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
