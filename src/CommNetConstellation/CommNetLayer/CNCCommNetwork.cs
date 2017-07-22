﻿using CommNet;
using System;
using CommNetCoopAPI;

namespace CommNetConstellation.CommNetLayer
{
    /// <summary>
    /// Extend the functionality of the KSP's CommNetwork (secondary model in the Model–view–controller sense)
    /// </summary>
    public class CNCCommNetwork : CommNetwork
    {
        private short publicFreq = CNCSettings.Instance.PublicRadioFrequency;

        /// <summary>
        /// Edit the connectivity between two potential nodes
        /// </summary>
        [CommNetCoopAttrAndOr(CommNetCoopAttrAndOr.options.AND)]
        [CommNetCoopAttrPrePost(CommNetCoopAttrPrePost.options.POST)]
        protected override bool SetNodeConnection(CommNode a, CommNode b)
        {
            short aFreq, bFreq;

            try
            {
                aFreq = (a.isHome) ? publicFreq : ((CNCCommNetVessel)CNCCommNetScenario.Instance.findCorrespondingVessel(a).Connection).getRadioFrequency();
                bFreq = (b.isHome) ? publicFreq : ((CNCCommNetVessel)CNCCommNetScenario.Instance.findCorrespondingVessel(b).Connection).getRadioFrequency();
            }
            catch(NullReferenceException e) // either CommNode could be a kerbal on EVA
            {
                this.Disconnect(a, b, true);
                return false;
            }

            if (aFreq != bFreq && aFreq != publicFreq && bFreq != publicFreq) //check if two nodes talk, using same non-public frequency
            {
                this.Disconnect(a, b, true);
                return false;
            }

            bool aMembershipFlag, bMembershipFlag;

            try
            {
                aMembershipFlag = (a.isHome) ? false : ((CNCCommNetVessel)CNCCommNetScenario.Instance.findCorrespondingVessel(a).Connection).getMembershipFlag();
                bMembershipFlag = (b.isHome) ? false : ((CNCCommNetVessel)CNCCommNetScenario.Instance.findCorrespondingVessel(b).Connection).getMembershipFlag();
            }
            catch(NullReferenceException e) // either CommNode could be a kerbal on EVA
            {
                this.Disconnect(a, b, true);
                return false;
            }

            if ((aMembershipFlag && aFreq != bFreq && aFreq != publicFreq) ||
                (bMembershipFlag && aFreq != bFreq && bFreq != publicFreq)) // check if either node has membership flag to talk to members only
            {
                this.Disconnect(a, b, true);
                return false;
            }

            return CommNetCoopChecker.CommNetCoopInstalled ? true : base.SetNodeConnection(a, b);
        }
    }
}
