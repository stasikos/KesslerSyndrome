﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.IO;

namespace KesslerSyndrome
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class DecayManager : MonoBehaviour
    {
        Dictionary<Vessel, double> nextDecay = new Dictionary<Vessel, double>();

        string savedPath = KSPUtil.ApplicationRootPath + "/saves/" + HighLogic.SaveFolder + "/Kessler.dat";

        private void Start()
        {
            if (!HighLogic.CurrentGame.Parameters.CustomParams<KesslerSettings>().orbitalDecay)
            {
                Debug.Log("[KesslerSyndrome]: DecayManager is turned off. Destroying instance");
                Destroy(this);
            }
            GameEvents.onGameStateSave.Add(onGameStateSave);
            GameEvents.onStageSeparation.Add(onStageSeparation);
            Debug.Log("[KesslerSyndrome]: DecayManager is awake");
            if (FlightGlobals.Vessels.Count == 0) return;
            List<Vessel> decayCandidates = new List<Vessel>();
            for (int i = 0; i <FlightGlobals.Vessels.Count; i++)
            {
                Vessel v = FlightGlobals.Vessels.ElementAt(i);
                if (v == null) continue;
                if (v == FlightGlobals.ActiveVessel) continue;
                if (!v.mainBody.atmosphere) continue;
                if (v.vesselType == VesselType.EVA || v.vesselType == VesselType.Flag || v.vesselType == VesselType.SpaceObject || v.vesselType == VesselType.Unknown) continue;
                if (!HighLogic.CurrentGame.Parameters.CustomParams<KesslerSettings>().allDecay && v.vesselType != VesselType.Debris) continue;
                if (v.Landed || v.Splashed) continue;
                if (v.altitude > v.mainBody.scienceValues.spaceAltitudeThreshold) continue;
                decayCandidates.Add(v);
            }
            Debug.Log("[KesslerSyndrome]: Finished populating decay candidates. Found " + decayCandidates.Count + " candidates");
            if (decayCandidates.Count == 0) return;
            try
            {
                Debug.Log("[KesslerSyndrome]: Catching up decay");
                ConfigNode node = ConfigNode.Load(savedPath);
                double d;
                for (int vc = 0; vc < decayCandidates.Count; vc++)
                {
                    Vessel v = decayCandidates.ElementAt(vc);
                    if (!double.TryParse(node.GetValue(v.id.ToString()), out d)) continue;
                    if (d >= Planetarium.GetUniversalTime()) continue;
                    double timeToCatchUp = Planetarium.GetUniversalTime() - d;
                    int orbitsToCatchUp = (int)timeToCatchUp / (int)v.orbit.period;
                    Debug.Log("[KesslerSyndrome]: " + v.id + " needs to catch up on " + orbitsToCatchUp + " orbits worth of decay");
                    if (orbitsToCatchUp == 0) continue;
                    double decay = 1.0f - (orbitsToCatchUp * GetDecayPercent(v));
                    if (decay < 0) decay = 0;
                    v.orbit.semiMajorAxis = v.orbit.semiMajorAxis * decay;
                    Debug.Log("[KesslerSyndrome]: Caught up with " + v.id + "'s decay");
                    nextDecay.Add(v, v.orbit.timeToPe + Planetarium.GetUniversalTime());
                }
            }
            catch
            {
                Debug.Log("[KesslerSyndrome]: Encountered a problem loading the decay manager. This message is harmless if this is the first time the decay manager has been loaded on this save");
            }
            finally
            {
                for (int vc = 0; vc < decayCandidates.Count; vc++)
                {
                    Vessel v = decayCandidates.ElementAt(vc);
                    double d;
                    if (nextDecay.TryGetValue(v, out d)) continue;
                    nextDecay.Add(v, v.orbit.timeToPe + Planetarium.GetUniversalTime());
                }
            }
        }

        private void onStageSeparation(EventReport data)
        {
            Debug.Log("[KesslerSyndrome]: Staging Event detected. Checking for new debris");
            for(int i = 0; i<FlightGlobals.Vessels.Count; i++)
            {
                Vessel v = FlightGlobals.Vessels.ElementAt(i);
                if (v == null) continue;
                double d;
                if (nextDecay.TryGetValue(v, out d)) continue;
                if (v == FlightGlobals.ActiveVessel) continue;
                if (!v.mainBody.atmosphere) continue;
                if (v.vesselType == VesselType.EVA || v.vesselType == VesselType.Flag || v.vesselType == VesselType.SpaceObject || v.vesselType == VesselType.Unknown) continue;
                if (!HighLogic.CurrentGame.Parameters.CustomParams<KesslerSettings>().allDecay && v.vesselType != VesselType.Debris) continue;
                if (v.Landed || v.Splashed) continue;
                if (v.altitude > v.mainBody.scienceValues.spaceAltitudeThreshold) continue;
                nextDecay.Add(v, Planetarium.GetUniversalTime() + v.orbit.timeToPe);
                Debug.Log("[KesslerSyndrome]: Added " + v.name + " to the decay list");
            }
        }

        private void onGameStateSave(ConfigNode data)
        {
            ConfigNode node = new ConfigNode();
            for (int i = 0; i < nextDecay.Count; i++)
            {
                var v = nextDecay.ElementAt(i);
                node.AddValue(v.Key.id.ToString(), v.Value);
            }
            node.Save(savedPath);
            Debug.Log("[KesslerSyndrome]: Saved Data");
        }

        private void Update()
        {
            if (nextDecay.Count == 0) return;
            for (int i = 0; i < nextDecay.Count; i++)
            {
                var v = nextDecay.ElementAt(i);
                if (v.Key == null)
                {
                    nextDecay.Remove(v.Key);
                    return;
                }
                if (v.Value > Planetarium.GetUniversalTime()) continue;
                v.Key.orbit.semiMajorAxis = v.Key.orbit.semiMajorAxis * (1.0f - GetDecayPercent(v.Key));
                Debug.Log("[KesslerSyndrome]: decayed " + v.Key.id + "'s orbit");
                nextDecay.Remove(v.Key);
                nextDecay.Add(v.Key, Planetarium.GetUniversalTime() + v.Key.orbit.timeToPe);
            }
        }

        double GetDecayPercent(Vessel v)
        {
            double decayDepth = v.orbit.PeA / v.mainBody.scienceValues.spaceAltitudeThreshold;
            decayDepth = 1.0f - decayDepth;
            decayDepth = decayDepth * HighLogic.CurrentGame.Parameters.CustomParams<KesslerSettings>().decayPercent;
            return decayDepth;
        }

        private void OnDestroy()
        {
            Debug.Log("[KesslerSyndrome]: Destroying DecayManager");
            if (GameEvents.onGameStateSave == null) return;
            GameEvents.onGameStateSave.Remove(onGameStateSave);
        }
    }
    }
