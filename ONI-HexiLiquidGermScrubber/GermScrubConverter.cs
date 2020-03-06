using Klei;
using Klei.AI;
using KSerialization;
using STRINGS;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;


namespace ONI_HexiLiquidGermScrubber {
    [SerializationConfig(MemberSerialization.OptIn)]
    public class GermScrubConverter : StateMachineComponent<GermScrubConverter.StatesInstance>, IEffectDescriptor
    {
        private static float MAX_KG_PER_SEC = 0.25f;
        private static float GERMS_PER_KG = 10000f;
        private static bool EMIT_CHLORINE = false;

        public static SimHashes filterIn = SimHashes.BleachStone;
        private static SimHashes filterOut = SimHashes.ChlorineGas;

        [MyCmpGet]
        private Operational operational;
        [MyCmpReq]
        private Storage storage;

        public bool showDescriptors = true;
        public Action<float> onConvertMass;
        private AttributeInstance machinerySpeedAttribute;
        private const float BASE_INTERVAL = 1f;
        protected static StatusItem GermScrubConverterInput;
        protected static StatusItem GermScrubConverterOutput;
        protected static StatusItem GermScrubConverterMisc;
        
        [SerializeField]
        private HandleVector<int>.Handle filterConsumedLastTick;
        [SerializeField]
        private HandleVector<int>.Handle fluidProcessedLastTick;

        private int inCell, outCell;

        public void SetStorage(Storage storage)
        {
            this.storage = storage;
        }

        public bool CanConvertAtAll() {
            float filterAmt = 0;
            float liquidAmt = 0;
            List<GameObject> items = this.storage.items;
            PrimaryElement elem = null;
            foreach (GameObject item in items) {
                elem = item.GetComponent<PrimaryElement>();
                if (elem.ElementID == SimHashes.BleachStone) filterAmt += elem.Mass;
                else if (elem.Element.IsLiquid /*&& elem.DiseaseIdx != Byte.MaxValue*/) liquidAmt += elem.Mass;
            }
            if (filterAmt <= 0.1f || liquidAmt <= 0.1f) return false; // non-zero to prevent constant activity
            ConduitFlow flowManager = Conduit.GetFlowManager(ConduitType.Liquid);
            return flowManager.HasConduit(outCell);
        }

        private float GetSpeedMultiplier()
        {
            return this.machinerySpeedAttribute.GetTotalValue();
        }

        private void ConvertMass()
        {
            float filterAmt = 0;
            List<GameObject> items = this.storage.items;
            PrimaryElement elem = null;
            foreach (GameObject item in items) {
                elem = item.GetComponent<PrimaryElement>();
                if (elem.ElementID == SimHashes.BleachStone) filterAmt += elem.Mass;
            }
            if (filterAmt <= 0) return;
            float maxGerms = Mathf.Min(GERMS_PER_KG * MAX_KG_PER_SEC, (int)(filterAmt * GERMS_PER_KG));
            float removedAmount = 0;
            ConduitFlow flowManager = Conduit.GetFlowManager(ConduitType.Liquid);
            if (!flowManager.HasConduit(inCell) || !flowManager.HasConduit(outCell)) return;
            foreach (GameObject item in items) {
                elem = item.GetComponent<PrimaryElement>();
                if (elem.Element.IsLiquid) {
                    float mass = Mathf.Min(10f, elem.Mass);
                    float disease = elem.DiseaseCount / elem.Mass * mass;
                    if (elem.DiseaseIdx == Byte.MaxValue) disease = 0;
                    if (disease > maxGerms) {
                        mass = mass * maxGerms / disease;
                        disease = maxGerms;
                    }
                    float trueMass = flowManager.AddElement(outCell, elem.ElementID, mass, elem.Temperature, Byte.MaxValue, 0);
                    if (trueMass < mass) {
                        disease = disease * trueMass / mass;
                        mass = trueMass;
                    }
                    elem.Mass -= mass;
                    elem.ModifyDiseaseCount(-(int)disease, "");
                    removedAmount = disease;
                    Game.Instance.accumulators.Accumulate(fluidProcessedLastTick, mass);
                    if (mass > 0) break;
                }
            }
            if (removedAmount > 0) {
                float removal = (float)removedAmount / GERMS_PER_KG;
                storage.ConsumeIgnoringDisease(ElementLoader.FindElementByHash(filterIn).tag, removal);
                Game.Instance.accumulators.Accumulate(filterConsumedLastTick, removal);
                if (EMIT_CHLORINE) {
                    float addition = (float)removedAmount / GERMS_PER_KG;
                    Element elementByHash = ElementLoader.FindElementByHash(filterOut);
                    Vector3 outVector3 = new Vector3(this.transform.GetPosition().x, this.transform.GetPosition().y, 0.0f);
                    int outCell = Grid.PosToCell(outVector3);
                    SimMessages.AddRemoveSubstance(outCell, filterOut, CellEventLogger.Instance.OxygenModifierSimUpdate, addition, 273.15f + 45f, Byte.MaxValue, 0);
                }
            }
            //TODO: find out what the nice name is
            this.storage.Trigger(-1697596308, (object) this.gameObject);
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            this.machinerySpeedAttribute = this.gameObject.GetAttributes().Add(Db.Get().Attributes.MachinerySpeed);
            if (GermScrubConverterInput == null) {
                Tag inElem = ElementLoader.FindElementByHash(filterIn).tag;
                GermScrubConverterInput = new StatusItem("ElementConverterInput", "BUILDING", string.Empty, StatusItem.IconType.Info, NotificationType.Neutral, true, OverlayModes.None.ID, true, 129022).SetResolveStringCallback((Func<string, object, string>) ((str, data) =>
                {
                    str = str.Replace("{ElementTypes}", inElem.ProperName());
                    str = str.Replace("{FlowRate}", GameUtil.GetFormattedByTag(inElem, Game.Instance.accumulators.GetAverageRate((HandleVector<int>.Handle)data), GameUtil.TimeSlice.PerSecond));
                    return str;
                }));
            }
            if (GermScrubConverterOutput == null && EMIT_CHLORINE) {
                Tag outElem = ElementLoader.FindElementByHash(filterOut).tag;
                GermScrubConverterOutput = new StatusItem("ElementConverterOutput", "BUILDING", string.Empty, StatusItem.IconType.Info, NotificationType.Neutral, true, OverlayModes.None.ID, true, 129022).SetResolveStringCallback((Func<string, object, string>) ((str, data) =>
                {
                    str = str.Replace("{ElementTypes}", outElem.ProperName());
                    str = str.Replace("{FlowRate}", GameUtil.GetFormattedByTag(outElem, Game.Instance.accumulators.GetAverageRate((HandleVector<int>.Handle)data), GameUtil.TimeSlice.PerSecond));
                    return str;
                }));
            }
            if (GermScrubConverterMisc == null) {
                GermScrubConverterMisc = new StatusItem("PumpingLiquidOrGas", "BUILDING", string.Empty, StatusItem.IconType.Info, NotificationType.Neutral, true, OverlayModes.None.ID, true, 129022).SetResolveStringCallback((Func<string, object, string>) ((str, data) =>
                {
                    str = str.Replace("{FlowRate}", GameUtil.GetFormattedByTag(GameTags.Liquid, Game.Instance.accumulators.GetAverageRate((HandleVector<int>.Handle)data), GameUtil.TimeSlice.PerSecond));
                    return str;
                }));
            }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            Building component = this.GetComponent<Building>();
            this.inCell = component.GetUtilityInputCell();
            this.outCell = component.GetUtilityOutputCell();
            filterConsumedLastTick = Game.Instance.accumulators.Add("ElementsConsumed", this);
            fluidProcessedLastTick = Game.Instance.accumulators.Add("OutputElements", this);
            this.smi.StartSM();
        }

        protected override void OnCleanUp()
        {
            base.OnCleanUp();
            Game.Instance.accumulators.Remove(filterConsumedLastTick);
            Game.Instance.accumulators.Remove(fluidProcessedLastTick);
        }

        public List<Descriptor> GetDescriptors(BuildingDef def)
        {
            List<Descriptor> descriptorList = new List<Descriptor>();
            string name = ElementLoader.FindElementByHash(filterIn).tag.ProperName();
            string inString = string.Format(STRINGS.UI.BUILDINGEFFECTS.ELEMENTCONSUMED, name, GameUtil.GetFormattedMass(0.25f, GameUtil.TimeSlice.PerSecond, GameUtil.MetricMassFormat.UseThreshold, true, "{0:0.##}"));
            descriptorList.Add(new Descriptor(inString, inString, Descriptor.DescriptorType.Requirement, false));
            descriptorList.Add(new Descriptor("All Diseases: -2500/s", "All Diseases: -2500/s", Descriptor.DescriptorType.Effect, false));
            return descriptorList;
        }

        public class StatesInstance : GameStateMachine<GermScrubConverter.States, GermScrubConverter.StatesInstance, GermScrubConverter, object>.GameInstance
        {
            private List<Guid> statusItemEntries = new List<Guid>();
            public StatesInstance(GermScrubConverter smi)
            : base(smi)
            {
            }
            public void AddStatusItems()
            {
                this.statusItemEntries.Add(this.master.GetComponent<KSelectable>().AddStatusItem(GermScrubConverter.GermScrubConverterInput, (object) this.master.filterConsumedLastTick));
                if (EMIT_CHLORINE) this.statusItemEntries.Add(this.master.GetComponent<KSelectable>().AddStatusItem(GermScrubConverter.GermScrubConverterOutput, (object) this.master.filterConsumedLastTick));
                this.statusItemEntries.Add(this.master.GetComponent<KSelectable>().AddStatusItem(GermScrubConverter.GermScrubConverterMisc, (object) this.master.fluidProcessedLastTick));
            }
            public void RemoveStatusItems()
            {
                foreach (Guid statusItemEntry in this.statusItemEntries)
                    this.master.GetComponent<KSelectable>().RemoveStatusItem(statusItemEntry, false);
                this.statusItemEntries.Clear();
            }
        }

        public class States : GameStateMachine<GermScrubConverter.States, GermScrubConverter.StatesInstance, GermScrubConverter>
        {
            public GameStateMachine<GermScrubConverter.States, GermScrubConverter.StatesInstance, GermScrubConverter, object>.State disabled;
            public GameStateMachine<GermScrubConverter.States, GermScrubConverter.StatesInstance, GermScrubConverter, object>.State converting;
            public override void InitializeStates(out StateMachine.BaseState default_state)
            {
                default_state = (StateMachine.BaseState) this.disabled;
                this.disabled.EventTransition(GameHashes.ActiveChanged, this.converting, (StateMachine<GermScrubConverter.States, GermScrubConverter.StatesInstance, GermScrubConverter, object>.Transition.ConditionCallback) (smi =>
                {
                    if (!((UnityEngine.Object) smi.master.operational == (UnityEngine.Object) null))
                    return smi.master.operational.IsActive;
                    return true;
                }));
                this.converting.Enter("AddStatusItems", (StateMachine<GermScrubConverter.States, GermScrubConverter.StatesInstance, GermScrubConverter, object>.State.Callback) (smi => smi.AddStatusItems())).Exit("RemoveStatusItems", (StateMachine<GermScrubConverter.States, GermScrubConverter.StatesInstance, GermScrubConverter, object>.State.Callback) (smi => smi.RemoveStatusItems())).EventTransition(GameHashes.ActiveChanged, this.disabled, (StateMachine<GermScrubConverter.States, GermScrubConverter.StatesInstance, GermScrubConverter, object>.Transition.ConditionCallback) (smi =>
                {
                    if ((UnityEngine.Object) smi.master.operational != (UnityEngine.Object) null)
                    return !smi.master.operational.IsActive;
                    return false;
                })).Update("ConvertMass", (Action<GermScrubConverter.StatesInstance, float>) ((smi, dt) => smi.master.ConvertMass()), UpdateRate.SIM_1000ms, true);
            }
        }
    }
}