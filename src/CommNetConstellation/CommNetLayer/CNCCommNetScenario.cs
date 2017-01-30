﻿using CommNet;
using Smooth.Algebraics;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CommNetConstellation.CommNetLayer
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, new GameScenes[] {GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.SPACECENTER })]
    public class CNCCommNetScenario : CommNetScenario
    {
        /* Note:
         * 1) On entering a desired scene, OnLoad() and then Start() are called.
         * 2) On leaving the scene, OnSave() is called
         * 3) GameScenes.SPACECENTER is recommended so that the constellation data can be verified and error-corrected in advance
         */

        private CNCCommNetUI CustomCommNetUI = null;
        //private CNCCommNetNetwork customNetworkService = null;
        public List<Constellation> constellations; // leave  the initialisation to OnLoad()

        public static new CNCCommNetScenario Instance
        {
            get;
            set;
        }

        protected override void Start()
        {
            CNCCommNetScenario.Instance = this;
            
            CommNetUI ui = FindObjectOfType<CommNetUI>();
            CustomCommNetUI = ui.gameObject.AddComponent<CNCCommNetUI>();
            UnityEngine.Object.Destroy(ui);

            CommNetNetwork.Instance.CommNet = new CNCCommNetwork();
            //customNetworkService = networkService.gameObject.AddComponent<CNCCommNetNetwork>();
            //UnityEngine.Object.Destroy(networkService);

            CommNetHome[] homes = FindObjectsOfType<CommNetHome>();
            for(int i=0; i<homes.Length; i++)
            {
                CNCCommNetHome customHome = homes[i].gameObject.AddComponent(typeof(CNCCommNetHome)) as CNCCommNetHome;
                customHome.copyOf(homes[i]);
                UnityEngine.Object.Destroy(homes[i]);
            }

            CommNetBody[] bodies = FindObjectsOfType<CommNetBody>();
            for (int i = 0; i < bodies.Length; i++)
            {
                CNCCommNetBody customBody = bodies[i].gameObject.AddComponent(typeof(CNCCommNetBody)) as CNCCommNetBody;
                customBody.copyOf(bodies[i]);
                UnityEngine.Object.Destroy(bodies[i]);
            }
        }

        public override void OnAwake()
        {
            //override to turn off CommNetScenario's instance check

            GameEvents.OnGameSettingsApplied.Add(new EventVoid.OnEvent(this.customResetNetwork));
        }

        private void OnDestroy()
        {
            //if (this.customNetworkService != null)
            //    UnityEngine.Object.Destroy(this.customNetworkService);

            if (this.CustomCommNetUI != null)
                UnityEngine.Object.Destroy(this.CustomCommNetUI);

            GameEvents.OnGameSettingsApplied.Remove(new EventVoid.OnEvent(this.customResetNetwork));
        }

        public void customResetNetwork()
        {
            CommNetNetwork.Instance.CommNet = new CNCCommNetwork();
            GameEvents.CommNet.OnNetworkInitialized.Fire();
        }

        public override void OnLoad(ConfigNode gameNode)
        {
            base.OnLoad(gameNode);
            CNCLog.Verbose("Scenario content to be read:\n{0}", gameNode);

            if (gameNode.HasNode("Constellations"))
            {
                ConfigNode rootNode = gameNode.GetNode("Constellations");
                ConfigNode[] constellationNodes = rootNode.GetNodes();

                if (constellationNodes.Length < 1) // missing constellation list
                {
                    CNCLog.Error("The 'Constellations' node is malformed! Reverted to the default constellation list.");
                    constellations = CNCSettings.Instance.Constellations;
                }
                else
                {
                    constellations = new List<Constellation>();

                    for (int i = 0; i < constellationNodes.Length; i++)
                    {
                        Constellation newConstellation = new Constellation();
                        ConfigNode.LoadObjectFromConfig(newConstellation, constellationNodes[i]);
                        constellations.Add(newConstellation);
                    }
                    ConfigNode.LoadObjectFromConfig(this, rootNode);
                }
            }
            else
            {
                CNCLog.Verbose("The 'Constellations' node is not found. The default constellation list is loaded.");
                constellations = CNCSettings.Instance.Constellations;
            }

            constellations.OrderBy(i => i.frequency);
        }

        public override void OnSave(ConfigNode gameNode)
        {
            if (constellations.Count < 1)
            {
                CNCLog.Error("The constellation list to save to persistent.sfs is empty!");
                base.OnSave(gameNode);
                return;
            }

            ConfigNode rootNode;
            if (!gameNode.HasNode("Constellations"))
            {
                rootNode = new ConfigNode("Constellations");
                gameNode.AddNode(rootNode);
            }
            else
            {
                rootNode = gameNode.GetNode("Constellations");
                rootNode.ClearNodes();
            }

            for (int i=0; i<constellations.Count; i++)
            {
                ConfigNode newConstellationNode = new ConfigNode("Constellation");
                newConstellationNode = ConfigNode.CreateConfigFromObject(constellations[i], newConstellationNode);
                rootNode.AddNode(newConstellationNode);
            }

            CNCLog.Verbose("Scenario content to be saved:\n{0}", gameNode);
            base.OnSave(gameNode);
        }

        public List<CNCCommNetVessel> getCommNetVessels(short targetRadioFrequency = -1)
        {
            List<Vessel> vessels = FlightGlobals.fetch.vessels;
            List<CNCCommNetVessel> commnetVessels = new List<CNCCommNetVessel>();

            for (int i = 0; i < vessels.Count; i++)
            {
                Vessel thisVessel = vessels[i];
                if (thisVessel.Connection != null)
                {
                    CNCCommNetVessel cncVessel = (CNCCommNetVessel)thisVessel.Connection;
                    if (cncVessel.getRadioFrequency() == targetRadioFrequency || targetRadioFrequency == -1)
                    {
                        commnetVessels.Add(cncVessel);
                    }
                }
            }

            return commnetVessels;
        }

        public Vessel findCorrespondingVessel(CommNode commNodeRef)
        {
            List<Vessel> allVessels = FlightGlobals.fetch.vessels;
            IEqualityComparer<CommNode> comparer = commNodeRef.Comparer;

            //brute-force search temporarily until I find a \omega(n) method
            for (int i = 0; i < allVessels.Count(); i++)
            {
                Vessel thisVessel = allVessels[i];
                if (thisVessel.connection != null)
                {
                    if (comparer.Equals(commNodeRef, thisVessel.connection.Comm))
                    {
                        return thisVessel;
                    }
                }
            }

            //not found
            return null;
        }
    }
}
