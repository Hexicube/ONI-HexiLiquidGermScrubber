using Database;
using Harmony;
using KSerialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TUNING;
using UnityEngine;

namespace ONI_HexiLiquidGermScrubber
{
    [HarmonyPatch(typeof(GeneratedBuildings), "LoadGeneratedBuildings")]
    internal class HexiLiquidGermScrubberMod
    {
        public static void Prefix()
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + HexiLiquidGermScrubberConfig.ID.ToUpper();
            Strings.Add(prefix + ".NAME", STRINGS.UI.FormatAsLink("Liquid Germ Scrubber", HexiLiquidGermScrubberConfig.ID));
            Strings.Add(prefix + ".DESC", "Removes germs from liquids.");
            /*Strings.Add(prefix + ".EFFECT", string.Format("Removes germs from piped in {0}.\n\nConsumes {1} and outputs {2}.",
            STRINGS.UI.FormatAsLink("liquids", "ELEMENTS_LIQUID"),
            STRINGS.UI.FormatAsLink("Bleach Stone", "BLEACHSTONE"),
            STRINGS.UI.FormatAsLink("Chlorine Gas", "CHLORINEGAS")));*/
            Strings.Add(prefix + ".EFFECT", string.Format("Removes germs from piped in {0}.\n\nConsumes {1}.",
            STRINGS.UI.FormatAsLink("liquids", "ELEMENTS_LIQUID"),
            STRINGS.UI.FormatAsLink("Bleach Stone", "BLEACHSTONE")));
            ModUtil.AddBuildingToPlanScreen("Medical", HexiLiquidGermScrubberConfig.ID);
        }
    }
    [HarmonyPatch(typeof(Db), "Initialize")]
    public static class InitHexiLiquidGermScrubber
    {
        public static void Prefix(Db __instance)
        {
            List<string> list = new List<string>(Techs.TECH_GROUPING["MedicineII"]) { HexiLiquidGermScrubberConfig.ID };
            Techs.TECH_GROUPING["MedicineII"] = list.ToArray();
        }
    }

    public class HexiLiquidGermScrubberConfig : IBuildingConfig {
        public const string ID = "HexiLiquidScrubber";

        public override BuildingDef CreateBuildingDef()
        {
            int width = 3;
            int height = 4;
            string anim = "hexiliquidscrubber_kanim";
            int hitpoints = 100;
            float construction_time = 30f;
            float melting_point = 800f;
            BuildingDef buildingDef = BuildingTemplates.CreateBuildingDef(
                ID, width, height, anim,
                hitpoints, construction_time, BUILDINGS.CONSTRUCTION_MASS_KG.TIER4, MATERIALS.ALL_METALS,
                melting_point, BuildLocationRule.OnFloor, BUILDINGS.DECOR.PENALTY.TIER2, NOISE_POLLUTION.NOISY.TIER3);
            buildingDef.RequiresPowerInput = true;
            buildingDef.EnergyConsumptionWhenActive = 300f;
            buildingDef.ExhaustKilowattsWhenActive = 0.0f;
            buildingDef.SelfHeatKilowattsWhenActive = 4f;
            buildingDef.InputConduitType = ConduitType.Liquid;
            buildingDef.OutputConduitType = ConduitType.Liquid;
            buildingDef.ViewMode = OverlayModes.LiquidConduits.ID;
            buildingDef.AudioCategory = "HollowMetal";
            buildingDef.PowerInputOffset = new CellOffset(1, 0);
            buildingDef.UtilityInputOffset = new CellOffset(-1, 3);
            buildingDef.UtilityOutputOffset = new CellOffset(-1, 1);
            buildingDef.PermittedRotations = PermittedRotations.FlipH;
            return buildingDef;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.IndustrialMachinery, false);
            Storage defaultStorage = BuildingTemplates.CreateDefaultStorage(go, false);
            defaultStorage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);
            defaultStorage.storageFilters = new List<Tag>(){ new Tag("BleachStone") };
            defaultStorage.capacityKg = 400f + 20f;
            go.AddOrGet<HexiLiquidGermScrubber>();
            Prioritizable.AddRef(go);
            GermScrubConverter elementConverter = go.AddOrGet<GermScrubConverter>();
            elementConverter.SetStorage(defaultStorage);
            ConduitConsumer conduitConsumer = go.AddOrGet<ConduitConsumer>();
            conduitConsumer.conduitType = ConduitType.Liquid;
            conduitConsumer.consumptionRate = 10f;
            conduitConsumer.capacityKG = 20f;
            conduitConsumer.capacityTag = GameTags.Liquid;
            conduitConsumer.forceAlwaysSatisfied = true;
            conduitConsumer.wrongElementResult = ConduitConsumer.WrongElementResult.Dump;
            ManualDeliveryKG manualDeliveryKg = go.AddComponent<ManualDeliveryKG>();
            manualDeliveryKg.SetStorage(defaultStorage);
            manualDeliveryKg.requestedItemTag = ElementLoader.FindElementByHash(SimHashes.BleachStone).tag;
            manualDeliveryKg.capacity = 400f;
            manualDeliveryKg.refillMass = 100f;
            manualDeliveryKg.choreTypeIDHash = Db.Get().ChoreTypes.FetchCritical.IdHash;
        }

        public override void DoPostConfigurePreview(BuildingDef def, GameObject go)
        {
            GeneratedBuildings.RegisterSingleLogicInputPort(go);
        }

        public override void DoPostConfigureUnderConstruction(GameObject go)
        {
            GeneratedBuildings.RegisterSingleLogicInputPort(go);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            GeneratedBuildings.RegisterSingleLogicInputPort(go);
            go.AddOrGet<LogicOperationalController>();
            go.AddOrGetDef<PoweredActiveController.Def>();
        }
    }
    
    [SerializationConfig(MemberSerialization.OptIn)]
    public class HexiLiquidGermScrubber : StateMachineComponent<HexiLiquidGermScrubber.StatesInstance>, ISim4000ms
    {
        public void Sim4000ms(float dt) {
            UpdateMeter();
        }

        [MyCmpGet]
        private Operational operational;
        private ManualDeliveryKG[] deliveryComponents;
        private MeterController oreMeter;

        private void UpdateMeter() {
            PrimaryElement elem = this.GetComponent<Storage>().FindPrimaryElement(GermScrubConverter.filterIn);
            if (elem == null) this.oreMeter.SetPositionPercent(0);
            else this.oreMeter.SetPositionPercent(Mathf.Clamp01(elem.Mass / 400f));
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            this.deliveryComponents = this.GetComponents<ManualDeliveryKG>();
            this.smi.StartSM();
            this.oreMeter = new MeterController((KAnimControllerBase)this.GetComponent<KBatchedAnimController>(), "target", "ore_meter_anim", Meter.Offset.Infront, Grid.SceneLayer.NoLayer, null);
            UpdateMeter();
        }

        public class StatesInstance : GameStateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.GameInstance
        {
            public StatesInstance(HexiLiquidGermScrubber smi) : base(smi)
            {
            }
        }

        public class States : GameStateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber>
        {
            public GameStateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.State off;
            public HexiLiquidGermScrubber.States.OnStates on;
            public override void InitializeStates(out StateMachine.BaseState default_state)
            {
                default_state = (StateMachine.BaseState) this.off;
                this.off.PlayAnim("off").EventTransition(GameHashes.OperationalChanged, (GameStateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.State) this.on, (StateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.Transition.ConditionCallback) (smi => smi.master.operational.IsOperational));
                this.on.PlayAnim("on").EventTransition(GameHashes.OperationalChanged, this.off, (StateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.Transition.ConditionCallback) (smi => !smi.master.operational.IsOperational)).DefaultState(this.on.waiting);
                this.on.waiting.EventTransition(GameHashes.OnStorageChange, this.on.working_pre, (StateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.Transition.ConditionCallback) (smi => smi.master.GetComponent<GermScrubConverter>().CanConvertAtAll()));
                this.on.working_pre.PlayAnim("working_pre").OnAnimQueueComplete(this.on.working);
                this.on.working.Enter((StateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.State.Callback) (smi => smi.master.operational.SetActive(true, false))).QueueAnim("working_loop", true, (Func<HexiLiquidGermScrubber.StatesInstance, string>) null).EventTransition(GameHashes.OnStorageChange, this.on.working_pst, (StateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.Transition.ConditionCallback) (smi => !smi.master.GetComponent<GermScrubConverter>().CanConvertAtAll())).Exit((StateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.State.Callback) (smi => smi.master.operational.SetActive(false, false)));
                this.on.working_pst.PlayAnim("working_pst").OnAnimQueueComplete(this.on.waiting);
            }
            public class OnStates : GameStateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.State
            {
                public GameStateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.State waiting;
                public GameStateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.State working_pre;
                public GameStateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.State working;
                public GameStateMachine<HexiLiquidGermScrubber.States, HexiLiquidGermScrubber.StatesInstance, HexiLiquidGermScrubber, object>.State working_pst;
            }
        }
    }
}
