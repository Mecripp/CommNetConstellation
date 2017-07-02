﻿using CommNet;
using CommNetConstellation.UI;
using Smooth.Algebraics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CommNetConstellation.CommNetLayer
{
    /// <summary>
    /// PartModule to be inserted into every part having ModuleCommand module (probe cores and manned cockpits)
    /// </summary>
    //This class is coupled with the MM patch (cnc_module_MM.cfg) that inserts CNConstellationModule into every command part
    public class CNConstellationModule : PartModule
    {
        [KSPEvent(guiActive = true, guiActiveEditor = false, guiActiveUnfocused = true, guiName = "CNC: Communication", active = true)]
        public void KSPEventVesselSetup()
        {             
            new VesselSetupDialog("Vessel - <color=#00ff00>Communication</color>", this.vessel, null).launch();
        }
    }

    /// <summary>
    /// PartModule to be inserted into every part having ModuleDataTransmitter module (antennas, probe cores and manned cockpits)
    /// </summary>
    //This class is coupled with the MM patch (cnc_module_MM.cfg) that inserts CNConstellationAntennaModule into every part
    public class CNConstellationAntennaModule : PartModule
    {
        [KSPField(isPersistant = true)] public short Frequency = CNCSettings.Instance.PublicRadioFrequency;
        [KSPField(isPersistant = true)] protected string OptionalName = "";
        [KSPField(isPersistant = true)] public bool InUse = true;

        public String Name
        {
            get { return (this.OptionalName.Length == 0) ? this.part.partInfo.title : this.OptionalName; }
            set { this.OptionalName = value; }
        }

        //TODO: auto-detect if antenna is deployed or retracted

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiActiveUnfocused = true, guiName = "CNC: Antenna Setup", active = true)]
        public void KSPEventAntennaConfig()
        {
            new AntennaSetupDialog("Antenna - <color=#00ff00>Setup</color>", this.vessel, this.part).launch();
        }
    }

    /// <summary>
    /// Independent-implementation data structure for an antenna part
    /// </summary>
    public class CNCAntennaPartInfo
    {
        public short frequency;
        public string name;
        public double antennaPower;
        public double antennaCombinableExponent;
        public bool antennaCombinable;
        public AntennaType antennaType;
        public uint GUID;
        public bool inUse; // selected by user to be used
        public bool canComm; //fixed and deployable antennas
    }

    /// <summary>
    /// Data structure for a CommNetVessel
    /// </summary>
    public class CNCCommNetVessel : CommNetVessel, IPersistenceSave, IPersistenceLoad
    {
        public enum FrequencyListOperation
        {
            AutoBuild,
            LockList,
            UpdateOnly
        };

        //http://forum.kerbalspaceprogram.com/index.php?/topic/141574-kspfield-questions/&do=findComment&comment=2625815
        // cannot be serialized
        protected Dictionary<short, double> FrequencyDict = new Dictionary<short, double>();
        [Persistent] private List<short> FreqDictionaryKeys = new List<short>();
        [Persistent] private List<double> FreqDictionaryValues = new List<double>();
        [Persistent] public FrequencyListOperation FreqListOperation = FrequencyListOperation.AutoBuild; // initial value

        protected short strongestFreq = -1;
        protected List<CNCAntennaPartInfo> vesselAntennas = new List<CNCAntennaPartInfo>();

        /// <summary>
        /// Retrieve the CNC data from the vessel
        /// </summary>
        protected override void OnNetworkInitialized()
        {
            base.OnNetworkInitialized();
            
            try
            {
                validateAndUpgrade(this.Vessel);
                OnAntennaChange();
            }
            catch (Exception e)
            {
                CNCLog.Error("Vessel '{0}' doesn't have any CommNet capability, likely a mislabelled junk or a kerbin on EVA", this.Vessel.GetName());
            }
        }

        /// <summary>
        /// Read the part data of an unloaded/loaded vessel and store in data structures
        /// </summary>
        protected List<CNCAntennaPartInfo> retrieveAllAntennas()
        {
            List<CNCAntennaPartInfo> antennas = new List<CNCAntennaPartInfo>();
            int numParts = (!this.vessel.loaded) ? this.vessel.protoVessel.protoPartSnapshots.Count : this.vessel.Parts.Count;

            //inspect each part
            for (int partIndex = 0; partIndex < numParts; partIndex++)
            {
                Part thisPart;
                ProtoPartSnapshot partSnapshot = null;

                if (this.Vessel.loaded)
                {
                    thisPart = this.vessel.Parts[partIndex];
                }
                else
                {
                    partSnapshot = this.vessel.protoVessel.protoPartSnapshots[partIndex];
                    thisPart = partSnapshot.partInfo.partPrefab;
                }

                bool populatedAntennaInfo = false;
                CNCAntennaPartInfo newAntennaPartInfo = new CNCAntennaPartInfo(); ;
                ProtoPartModuleSnapshot partModuleSnapshot = null;

                //inspect each module of the part
                for (int moduleIndex = 0; moduleIndex < thisPart.Modules.Count; moduleIndex++)
                {
                    PartModule thisPartModule = thisPart.Modules[moduleIndex];

                    if (thisPartModule is CNConstellationAntennaModule) // is it CNConstellationAntennaModule?
                    {
                        if (!this.Vessel.loaded)
                        {
                            partModuleSnapshot = partSnapshot.FindModule(thisPartModule, moduleIndex);

                            newAntennaPartInfo.frequency = short.Parse(partModuleSnapshot.moduleValues.GetValue("Frequency"));
                            string oname = partModuleSnapshot.moduleValues.GetValue("OptionalName");
                            newAntennaPartInfo.name = (oname.Length == 0) ? partSnapshot.partInfo.title : oname;
                            newAntennaPartInfo.inUse = bool.Parse(partModuleSnapshot.moduleValues.GetValue("InUse"));
                        }
                        else
                        {
                            CNConstellationAntennaModule antennaMod = (CNConstellationAntennaModule)thisPartModule;
                            newAntennaPartInfo.frequency = antennaMod.Frequency;
                            newAntennaPartInfo.name = antennaMod.Name;
                            newAntennaPartInfo.inUse = antennaMod.InUse;
                        }

                        populatedAntennaInfo = true;
                    }
                    else if (thisPartModule is ICommAntenna) // is it ModuleDataTransmitter?
                    {
                        ICommAntenna thisAntenna = thisPartModule as ICommAntenna;

                        if (!this.Vessel.loaded)
                            partModuleSnapshot = partSnapshot.FindModule(thisPartModule, moduleIndex);

                        newAntennaPartInfo.antennaPower = (!this.vessel.loaded) ? thisAntenna.CommPowerUnloaded(partModuleSnapshot) : thisAntenna.CommPower;
                        newAntennaPartInfo.antennaCombinable = thisAntenna.CommCombinable;
                        newAntennaPartInfo.antennaCombinableExponent = thisAntenna.CommCombinableExponent;
                        newAntennaPartInfo.antennaType = thisAntenna.CommType;
                        newAntennaPartInfo.GUID = thisPart.craftID; // good enough
                        newAntennaPartInfo.canComm = (!this.vessel.loaded) ? thisAntenna.CanCommUnloaded(partModuleSnapshot) : thisAntenna.CanComm();

                        populatedAntennaInfo = true;
                    }
                }

                if(populatedAntennaInfo) // valid info?
                    antennas.Add(newAntennaPartInfo);
            }

            return antennas;
        }

        /// <summary>
        /// Build the vessel's frequency list from chosen antennas
        /// </summary>
        protected Dictionary<short, double> buildFrequencyList(List<CNCAntennaPartInfo> antennas)
        {
            Dictionary<short, double> freqDict = new Dictionary<short, double>();
            Dictionary<short, double[]> powerDict = new Dictionary<short, double[]>();

            const int COMINDEX = 0;
            const int MAXINDEX = 1;

            //read each antenna
            for(int i=0; i<antennas.Count; i++)
            {
                if (!antennas[i].inUse || !antennas[i].canComm) // deselected or retracted
                    continue;

                if(!powerDict.ContainsKey(antennas[i].frequency))//not found
                    powerDict.Add(antennas[i].frequency, new double[] { 0.0, 0.0 });

                if (antennas[i].antennaCombinable) // TODO: revise to best antenna power * (total power / best power) * avg(all expo)
                    powerDict[antennas[i].frequency][COMINDEX] += (powerDict[antennas[i].frequency][COMINDEX]==0.0) ? antennas[i].antennaPower : antennas[i].antennaCombinableExponent * antennas[i].antennaPower;
                else
                    powerDict[antennas[i].frequency][MAXINDEX] = Math.Max(powerDict[antennas[i].frequency][MAXINDEX], antennas[i].antennaPower);
            }

            //consolidate into vessel's list of frequencies and their com powers
            foreach (short freq in powerDict.Keys)
            {
                freqDict.Add(freq, powerDict[freq].Max());
            }

            return freqDict;
        }

        /// <summary>
        /// Get the list of frequencies only
        /// </summary>
        public List<short> getFrequencies()
        {
            return this.FrequencyDict.Keys.ToList();
        }

        /// <summary>
        /// Get the max com power of a given frequency
        /// </summary>
        public double getMaxComPower(short frequency)
        {
            if (this.FrequencyDict.ContainsKey(frequency))
                return this.FrequencyDict[frequency];
            else
                return 0.0;
        }

        /// <summary>
        /// Get the frequency of the largest Com Power
        /// </summary>
        public short getStrongestFrequency()
        {
            if (this.strongestFreq < 0)
            {
                this.FrequencyDict = buildFrequencyList(this.vesselAntennas);
                this.strongestFreq = computeStrongestFrequency(this.FrequencyDict);
            }

            return this.strongestFreq;
        }

        /// <summary>
        /// Find the frequency with the largest Com Power
        /// </summary>
        private short computeStrongestFrequency(Dictionary<short, double> dict)
        {
            short freq = -1;
            double power = 0;
            foreach(short key in dict.Keys)
            {
                if (power < dict[key])
                {
                    power = dict[key];
                    freq = key;
                }
            }

            //if (freq == -1)
            //    CNCLog.Error("No frequency found on this vessel '{0}'", this.Vessel.vesselName);

            return freq;
        }

        public bool canUpdateFreqList()
        {
            return this.FreqListOperation != CNCCommNetVessel.FrequencyListOperation.LockList;
        }

        /// <summary>
        /// Notify CommNet vessel on antenna change (like changing frequency and deploy/retract antenna)
        /// </summary>
        public void OnAntennaChange(uint GUID = 0, bool antennaDeployment = true, short newFrequency = -1)
        {
            this.vesselAntennas = retrieveAllAntennas();

            switch (this.FreqListOperation)
            {
                case FrequencyListOperation.AutoBuild:
                    this.FrequencyDict = buildFrequencyList(this.vesselAntennas);
                    this.strongestFreq = computeStrongestFrequency(this.FrequencyDict);
                    break;
                case FrequencyListOperation.LockList: // dont change current freq dict
                    this.strongestFreq = computeStrongestFrequency(this.FrequencyDict);
                    ScreenMessages.PostScreenMessage(new ScreenMessage("Note: Lock List mode is in effect.", CNCSettings.ScreenMessageDuration, ScreenMessageStyle.UPPER_LEFT));
                    break;
                case FrequencyListOperation.UpdateOnly:
                    //TODO: complete updateonly function
                    break;
            }
        }

        /// <summary>
        /// Rebuild the frequency list from all antennas
        /// </summary>
        public void rebuildFreqList()
        {
            this.vesselAntennas = retrieveAllAntennas();
            this.FrequencyDict = buildFrequencyList(this.vesselAntennas);
            this.strongestFreq = computeStrongestFrequency(this.FrequencyDict);
        }

        /// <summary>
        /// Add a new (or existing) frequency and its comm power to vessel's frequency list
        /// </summary>
        public void addToFreqList(short frequency, double commPower)
        {
            if (this.FrequencyDict.ContainsKey(frequency))
                this.FrequencyDict[frequency] = commPower;
            else
                this.FrequencyDict.Add(frequency, commPower);
        }

        /// <summary>
        /// Drop the specific frequency from the vessel's frequency list
        /// </summary>
        public void removeFromFreqList(short frequency)
        {
            this.FrequencyDict.Remove(frequency);
        }

        /// <summary>
        /// Clear the vessel's frequency list
        /// </summary>
        public void clearFreqList()
        {
            this.FrequencyDict.Clear();
        }

        /// <summary>
        /// Replace one frequency in the particular antenna
        /// </summary>
        public bool replaceFrequency(uint GUID, short newFrequency)
        {
            try
            {
                if (!Constellation.isFrequencyValid(newFrequency))
                    throw new Exception(string.Format("The new frequency {0} is out of the range [0,{1}]!", newFrequency, short.MaxValue));

                if (this.Vessel.loaded)
                {
                    CNConstellationAntennaModule mod = this.Vessel.FindPartModulesImplementing<CNConstellationAntennaModule>().Find(x => x.part.craftID == GUID);
                    mod.Frequency = newFrequency;
                }
                else
                {
                    ProtoPartSnapshot part = this.vessel.protoVessel.protoPartSnapshots.Find(x => x.partInfo.partPrefab.craftID == GUID);
                    ProtoPartModuleSnapshot cncAntMod = part.FindModule("CNConstellationAntennaModule");
                    cncAntMod.moduleValues.SetValue("Frequency", newFrequency);
                }
            }
            catch (Exception e)
            {
                CNCLog.Error("Error encounted when updating CommNet vessel '{0}''s frequency to {2}: {1}", this.Vessel.GetName(), e.Message, newFrequency);
                return false;
            }

            OnAntennaChange();

            CNCLog.Debug("Update CommNet vessel '{0}''s frequency to {1}", this.Vessel.GetName(), newFrequency);
            return true;
        }

        /// <summary>
        /// Replace one frequency in all antennas
        /// </summary>
        public bool replaceFrequency(short oldFrequency, short newFrequency)
        {
            try
            {
                if (!Constellation.isFrequencyValid(newFrequency))
                    throw new Exception(string.Format("The new frequency {0} is out of the range [0,{1}]!", newFrequency, short.MaxValue));

                if (this.Vessel.loaded)
                {
                    List<CNConstellationAntennaModule> mods = this.Vessel.FindPartModulesImplementing<CNConstellationAntennaModule>();
                    for (int i = 0; i < mods.Count; i++)
                    {
                        if (mods[i].Frequency == oldFrequency)
                            mods[i].Frequency = newFrequency;
                    }
                }
                else
                {
                    for (int i = 0; i < this.vessel.protoVessel.protoPartSnapshots.Count; i++)
                    {
                        ProtoPartSnapshot part = this.vessel.protoVessel.protoPartSnapshots[i];
                        ProtoPartModuleSnapshot cncAntMod;

                        if ((cncAntMod = part.FindModule("CNConstellationAntennaModule")) != null)
                        {
                            if (short.Parse(cncAntMod.moduleValues.GetValue("Frequency")) == oldFrequency)
                                cncAntMod.moduleValues.SetValue("Frequency", newFrequency);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                CNCLog.Error("Error encounted when updating CommNet vessel '{0}''s frequency {2} to {3}: {1}", this.Vessel.GetName(), e.Message, oldFrequency, newFrequency);
                return false;
            }

            OnAntennaChange();

            CNCLog.Debug("Update CommNet vessel '{0}''s frequency {1} to {2}", this.Vessel.GetName(), oldFrequency, newFrequency);
            return true;
        }

        /// <summary>
        /// Independent-implementation information on all antennas of the vessel
        /// </summary>
        public List<CNCAntennaPartInfo> getAllAntennaInfo(bool readAntennaData = false)
        {
            if (readAntennaData)
                this.vesselAntennas = retrieveAllAntennas();

            return this.vesselAntennas;
        }

        public void toggleAntenna(uint GUID, bool inUse)
        {
            CNCAntennaPartInfo partInfo = this.vesselAntennas.Find(x => x.GUID == GUID);
            if(partInfo != null)
            {
                partInfo.inUse = inUse;

                int numParts = (!this.vessel.loaded) ? this.vessel.protoVessel.protoPartSnapshots.Count : this.vessel.Parts.Count;
                for (int partIndex = 0; partIndex < numParts; partIndex++) // TODO: simplify this
                {
                    if (this.Vessel.loaded)
                    {
                        if (this.vessel.Parts[partIndex].craftID == GUID)
                        {
                            this.vessel.Parts[partIndex].FindModuleImplementing<CNConstellationAntennaModule>().InUse = inUse;
                            break;
                        }
                    }
                    else
                    {
                        ProtoPartSnapshot partSnapshot = this.vessel.protoVessel.protoPartSnapshots[partIndex];
                        if (partSnapshot.partInfo.partPrefab.craftID == GUID)
                        {
                            ProtoPartModuleSnapshot cncAntMod;
                            if ((cncAntMod = partSnapshot.FindModule("CNConstellationAntennaModule")) != null)
                                cncAntMod.moduleValues.SetValue("InUse", inUse);
                            break;
                        }
                    }
                }
            }
            else
            {
                CNCLog.Error("Cannot find the antenna with GUID {0} to set to be used or not!", GUID);
            }
        }

        /// <summary>
        /// Check if given vessel has CNConstellationModule and its attributes required, and if not, "upgrade" the vessel data
        /// </summary>
        public void validateAndUpgrade(Vessel thisVessel)
        {
            if (thisVessel == null)
                return;
            if (thisVessel.loaded) // it seems KSP will automatically add/upgrade the active vessel (unconfirmed)
                return;

            CNCLog.Debug("Unloaded CommNet vessel '{0}' is validated and upgraded", thisVessel.GetName());

            if (thisVessel.protoVessel != null)
            {
                List<ProtoPartSnapshot> parts = thisVessel.protoVessel.protoPartSnapshots;
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i].FindModule("ModuleCommand") != null) // check command parts only
                    {
                        ProtoPartModuleSnapshot cncModule;
                        if ((cncModule = parts[i].FindModule("CNConstellationModule")) == null) //check if CNConstellationModule is there
                        {
                            CNConstellationModule realcncModule = gameObject.AddComponent<CNConstellationModule>(); // don't use new keyword. PartModule is Monobehavior
                            parts[i].modules.Add(new ProtoPartModuleSnapshot(realcncModule));

                            CNCLog.Verbose("CNConstellationModule is added to CommNet Vessel '{0}'", thisVessel.GetName());
                        }
                        else //check if all attributes are or should not be there
                        {
                            if (cncModule.moduleValues.HasValue("radioFrequency")) //obsolete
                                cncModule.moduleValues.RemoveValue("radioFrequency");

                            if (cncModule.moduleValues.HasValue("communicationMembershipFlag")) //obsolete
                                cncModule.moduleValues.RemoveValue("communicationMembershipFlag");
                        }
                    }

                    if (parts[i].FindModule("ModuleDataTransmitter") != null) // check antennas, probe cores and manned cockpits
                    {
                        ProtoPartModuleSnapshot cncModule;
                        if ((cncModule = parts[i].FindModule("CNConstellationAntennaModule")) == null) //check if CNConstellationAntennaModule is there
                        {
                            CNConstellationAntennaModule realcncModule = gameObject.AddComponent<CNConstellationAntennaModule>(); // don't use new keyword. PartModule is Monobehavior
                            parts[i].modules.Add(new ProtoPartModuleSnapshot(realcncModule));

                            CNCLog.Verbose("CNConstellationAntennaModule is added to CommNet Vessel '{0}'", thisVessel.GetName());
                        }
                    }
                } // end of part loop
            }
        }

        protected override void OnSave(ConfigNode gameNode)
        {
            base.OnSave(gameNode);

            if (gameNode.HasNode(GetType().FullName))
                gameNode.RemoveNode(GetType().FullName);

            gameNode.AddNode(ConfigNode.CreateConfigFromObject(this));
        }

        protected override void OnLoad(ConfigNode gameNode)
        {
            base.OnLoad(gameNode);

            if(gameNode.HasNode(GetType().FullName))
                ConfigNode.LoadObjectFromConfig(this, gameNode.GetNode(GetType().FullName));
        }

        public void PersistenceSave()
        {
            FreqDictionaryKeys = FrequencyDict.Keys.ToList();
            FreqDictionaryValues = FrequencyDict.Values.ToList();
        }

        public void PersistenceLoad()
        {
            FrequencyDict = Enumerable.Range(0, FreqDictionaryKeys.Count).ToDictionary(idx => FreqDictionaryKeys[idx], idx => FreqDictionaryValues[idx]);
        }
    }
}
