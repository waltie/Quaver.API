using Quaver.API.Maps;
using Quaver.API.Qss.Structures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Quaver.API.Qss
{
    /// <summary>
    ///     Will be used to solve Strain Rating.
    /// </summary>
    public class StrainRatingData
    {
        /// <summary>
        ///     Size of each graph partition in miliseconds
        /// </summary>
        public const int GRAPH_INTERVAL_SIZE_MS = 500;

        /// <summary>
        ///     Offset between each graph partition in miliseconds
        /// </summary>
        public const int GRAPH_INTERVAL_OFFSET_MS = 100;

        private const float THRESHOLD_LN_END_MS = 42;
        private const float THRESHOLD_CHORD_CHECK_MS = 8;

        /// <summary>
        ///     Total ammount of milliseconds in a second.
        /// </summary>
        public const float SECONDS_TO_MILLISECONDS = 1000;

        /// <summary>
        ///     Map that will be referenced for calculation
        /// </summary>
        public Qua Qua { get; private set; }

        /// <summary>
        ///     Overall difficulty of the map
        /// </summary>
        public float OverallDifficulty { get; private set; } = 0;

        /// <summary>
        ///     Average note density of the map
        /// </summary>
        public float AverageNoteDensity { get; private set; } = 0;

        /// <summary>
        ///     Hit objects in the map used for solving difficulty
        /// </summary>
        public List<HitObjectData> HitObjects { get; private set; } = new List<HitObjectData>();

        /// <summary>
        ///     Hit objects that will be played with the left hand
        /// </summary>
        public List<HitObjectData> LeftHandObjects { get; private set; } = new List<HitObjectData>();

        /// <summary>
        ///     Hit objects that will be played with the right hand
        /// </summary>
        public List<HitObjectData> RightHandObjects { get; private set; } = new List<HitObjectData>();

        /// <summary>
        ///     Hit objects that can be played with either right or left hand. Used for even keyed keymodes (5K/7K)
        /// </summary>
        public List<HitObjectData> AmbiguousHandObjects { get; private set; } = new List<HitObjectData>();

        /// <summary>
        ///     Assumes that the assigned hand will be the one to press that key
        /// </summary>
        private Dictionary<int, Hand> LaneToHand4K { get; set; } = new Dictionary<int, Hand>()
        {
            { 1, Hand.Left },
            { 2, Hand.Left },
            { 3, Hand.Right },
            { 4, Hand.Right }
        };

        /// <summary>
        ///     Assumes that the assigned hand will be the one to press that key
        /// </summary>
        private Dictionary<int, Hand> LaneToHand7K { get; set; } = new Dictionary<int, Hand>()
        {
            { 1, Hand.Left },
            { 2, Hand.Left },
            { 3, Hand.Left },
            { 4, Hand.Ambiguous },
            { 5, Hand.Right },
            { 6, Hand.Right },
            { 7, Hand.Right }
        };

        /// <summary>
        ///     Assumes that the assigned finger will be the one to press that key.
        /// </summary>
        private Dictionary<int, FingerState> LaneToFinger4K { get; set; } = new Dictionary<int, FingerState>()
        {
            { 1, FingerState.Middle },
            { 2, FingerState.Index },
            { 3, FingerState.Index },
            { 4, FingerState.Middle }
        };

        /// <summary>
        ///     Assumes that the assigned finger will be the one to press that key.
        /// </summary>
        private Dictionary<int, FingerState> LaneToFinger7K { get; set; } = new Dictionary<int, FingerState>()
        {
            { 1, FingerState.Ring },
            { 2, FingerState.Middle },
            { 3, FingerState.Index },
            { 4, FingerState.Thumb },
            { 5, FingerState.Index },
            { 6, FingerState.Middle },
            { 7, FingerState.Ring }
        };

        /// <summary>
        ///     const
        /// </summary>
        /// <param name="qua"></param>
        public StrainRatingData(Qua qua)
        {
            // Assign reference qua
            Qua = qua;

            // Don't bother calculating map difficulty if there's less than 2 hit objects
            if (Qua.HitObjects.Count < 2) return;

            // Solve for difficulty
            ComputeNoteDensityData();
            ComputeBaseStrainStates();
            ComputeForChords();
            ComputeFingerActions();
            ComputeActionPatterns();
            CalculateOverallDifficulty();
        }

        /// <summary>
        ///     Compute and generate Note Density Data.
        /// </summary>
        /// <param name="qssData"></param>
        /// <param name="qua"></param>
        private void ComputeNoteDensityData()
        {
            //MapLength = Qua.Length;
            AverageNoteDensity = SECONDS_TO_MILLISECONDS * Qua.HitObjects.Count / Qua.Length;

            //todo: solve note density graph
        }

        /// <summary>
        ///     Get Note Data, and compute the base strain weights
        ///     The base strain weights are affected by LN layering
        /// </summary>
        /// <param name="qssData"></param>
        /// <param name="qua"></param>
        private void ComputeBaseStrainStates()
        {
            // Variables for solving LN
            var checkLane = new List<bool>();
            var prevNoteIndex = new List<int>();

            // create list for lane check
            switch (Qua.Mode)
            {
                case Enums.GameMode.Keys4:
                    for (var i = 0; i < 4; i++)
                    {
                        checkLane.Add(false);
                        prevNoteIndex.Add(0);
                    }
                    break;
                case Enums.GameMode.Keys7:
                    for (var i = 0; i < 7; i++)
                    {
                        checkLane.Add(false);
                        prevNoteIndex.Add(0);
                    }
                    break;
            }

            // Add hit objects from qua map to qssData
            HitObjectData hitObjectData;
            for (var i = 0; i < Qua.HitObjects.Count; i++)
            {
                hitObjectData = new HitObjectData()
                {
                    StartTime = Qua.HitObjects[i].StartTime,
                    EndTime = Qua.HitObjects[i].EndTime,
                    Lane = Qua.HitObjects[i].Lane
                };

                // Current lane index
                var laneIndex = Qua.HitObjects[i].Lane - 1;
                prevNoteIndex[laneIndex] = i;

                // Mark everylane for checking except the current lane
                for (var j = 0; j < checkLane.Count; j++) checkLane[j] = false;
                checkLane[laneIndex] = true;

                // Solve LN
                for (var j = prevNoteIndex[laneIndex]; j < Qua.HitObjects.Count; j++)
                {

                    // Ignore if current lane is already checked
                    if (!checkLane[Qua.HitObjects[j].Lane - 1])
                        continue;

                    // Break loop if note is way paste LN end
                    if (Qua.HitObjects[j].StartTime > hitObjectData.EndTime + THRESHOLD_LN_END_MS)
                        break;

                    // Continue this iteration if note starts after LN start
                    if (Qua.HitObjects[j].StartTime >= hitObjectData.StartTime - THRESHOLD_CHORD_CHECK_MS)
                        continue;

                    // Determine 
                    // Target hitobject's LN ends after current hitobject's LN
                    if (Qua.HitObjects[j].EndTime > hitObjectData.EndTime)
                        hitObjectData.LnStrainMultiplier *= 1.2f; //TEMP STRAIN MULTIPLIER. use constant later.

                    // Target hitobject's LN ends before current hitobject's LN
                    else if (Qua.HitObjects[j].EndTime > 0)
                        hitObjectData.LnStrainMultiplier *= 1.2f; //TEMP STRAIN MULTIPLIER. use constant later.

                    // Target hitobject is not an LN
                    else
                        hitObjectData.LnStrainMultiplier *= 1.2f; //TEMP STRAIN MULTIPLIER. use constant later.

                    // Update lane checking variables
                    checkLane[Qua.HitObjects[j].Lane - 1] = true;
                    prevNoteIndex[Qua.HitObjects[j].Lane - 1] = j;

                    // Check to see if we should still search for more chord objects
                    var continueLoop = false;
                    foreach (var k in checkLane)
                        if (k)
                        {
                            continueLoop = true;
                            break;
                    }
                    if (!continueLoop) break;
                }

                // Assign Finger and Hand States
                switch (Qua.Mode)
                {
                    case Enums.GameMode.Keys4:
                        hitObjectData.FingerState = LaneToFinger4K[hitObjectData.Lane];
                        hitObjectData.Hand = LaneToHand4K[hitObjectData.Lane];
                        break;
                    case Enums.GameMode.Keys7:
                        hitObjectData.FingerState = LaneToFinger7K[hitObjectData.Lane];
                        hitObjectData.Hand = LaneToHand7K[hitObjectData.Lane];
                        break;
                }

                HitObjects.Add(hitObjectData);
            }
        }

        /// <summary>
        ///     Iterate through the HitObject list and assign HitObjects to each chord
        /// </summary>
        private void ComputeForChords()
        {
            // variables for solving chords
            var checkLane = new List<bool>();
            var prevNoteIndex = new List<int>();

            // create list for lane check
            switch (Qua.Mode)
            {
                case Enums.GameMode.Keys4:
                    for (var i = 0; i < 4; i++)
                    {
                        checkLane.Add(false);
                        prevNoteIndex.Add(0);
                    }
                    break;
                case Enums.GameMode.Keys7:
                    for (var i = 0; i < 7; i++)
                    {
                        checkLane.Add(false);
                        prevNoteIndex.Add(0);
                    }
                    break;
            }

            // Search through whole hit object list and find chords
            for (var i = 0; i < Qua.HitObjects.Count; i++)
            {
                // Current lane index
                var laneIndex = Qua.HitObjects[i].Lane - 1;
                prevNoteIndex[laneIndex] = i;

                // Mark everylane for checking except the current lane
                for (var j = 0; j < checkLane.Count; j++) checkLane[j] = false;
                checkLane[laneIndex] = true;

                // Search for chords
                for (var j = prevNoteIndex[laneIndex]; j < Qua.HitObjects.Count; j++)
                {
                    // Ignore if current lane is already checked
                    if (!checkLane[Qua.HitObjects[j].Lane - 1]) continue;

                    // How far apart the two notes are
                    var msDiff = Qua.HitObjects[j].StartTime - Qua.HitObjects[i].StartTime;

                    // Break loop if current object is over check threshold
                    if (msDiff >= THRESHOLD_CHORD_CHECK_MS) break;

                    // Check if the object is inside the hit window
                    if (Math.Abs(msDiff) < THRESHOLD_CHORD_CHECK_MS)
                    {
                        HitObjects[i].LinkedChordedHitObjects.Add(HitObjects[j]);
                        checkLane[Qua.HitObjects[j].Lane - 1] = true;
                        prevNoteIndex[Qua.HitObjects[j].Lane - 1] = j;
                    }

                    // Check to see if we should still search for more chord objects
                    var continueLoop = false;
                    foreach (var k in checkLane)
                        if (k)
                        {
                            continueLoop = true;
                            break;
                        }
                    if (!continueLoop) break;
                }
            }
        }

        //todo: remove this later. TEMP
        public int Roll { get; set; } = 0;
        public int SJack { get; set; } = 0;
        public int TJack { get; set; } = 0;
        public int Bracket { get; set; } = 0;

        /// <summary>
        ///     Scans every finger state, and determines its action (JACK/TRILL/TECH, ect).
        ///     Action-Strain multiplier is applied in computation.
        /// </summary>
        /// <param name="qssData"></param>
        private void ComputeFingerActions()
        {
            // Solve for HandChord (More than 2 keys pressed on a single hand
            for (var i = 0; i < HitObjects.Count; i++)
            {
                for (var j = 0; j < HitObjects[i].LinkedChordedHitObjects.Count; j++)
                {
                    if (HitObjects[i].LinkedChordedHitObjects[j].Hand == HitObjects[i].Hand)
                    {
                        HitObjects[i].HandChord = true;
                        HitObjects[i].HandChordStateIndex += (byte)Math.Pow((int)HitObjects[j].FingerState, 2);
                    }
                }
            }

            // Solve for Finger Action
            for (var i = 0; i < HitObjects.Count - 1;  i++)
            {
                var curHitOb = HitObjects[i];

                // Find the next Hit Object in the current Hit Object's Hand
                for (var j = i + 1; j < HitObjects.Count; j++)
                {
                    var nextHitOb = HitObjects[j];
                    if (curHitOb.Hand == nextHitOb.Hand)
                        continue;

                    if (nextHitOb.StartTime > curHitOb.StartTime + THRESHOLD_CHORD_CHECK_MS)
                        continue;

                    // Determined by if there's a minijack within 2 set of chords/single notes
                    var actionJackFound = (b & (1 << curHitOb.HandChordStateIndex - nextHitOb.HandChordStateIndex)) != 0;

                    // Determined by if a chord is found in either finger state
                    var actionChordFound = curHitOb.HandChord || nextHitOb.HandChord;

                    // Determined by if both fingerstates are exactly the same
                    var actionSameState = curHitOb.HandChordStateIndex == nextHitOb.HandChordStateIndex;

                    // Determined by how long the current finger action is
                    var actionDuration = nextHitOb.StartTime - curHitOb.StartTime;
                    FingerAction curAction;

                    // Trill/Roll
                    if (!actionChordFound && !actionSameState)
                    {
                        curAction = FingerAction.Roll;
                        Roll++;
                    }

                    // Simple Jack
                    else if (actionSameState)
                    {
                        curAction = FingerAction.SimpleJack;
                        SJack++;
                    }

                    // Tech Jack
                    else if (actionJackFound)
                    {
                        curAction = FingerAction.TechnicalJack;
                        TJack++;
                    }

                    // Bracket
                    else
                    {
                        curAction = FingerAction.Bracket;
                        Bracket++;
                    }

                    //Assign current finger action to hit object
                    curHitOb.FingerAction = curAction;

                    break;
                }
            }

        }

        /// <summary>
        ///     Scans every finger action and compute a pattern multiplier.
        ///     Pattern manipulation, and inflated patterns are factored into calculation.
        /// </summary>
        /// <param name="qssData"></param>
        private void ComputeActionPatterns()
        {

        }

        /// <summary>
        ///     Calculates the general difficulty of a beatmap
        /// </summary>
        /// <param name="qssData"></param>
        private void CalculateOverallDifficulty()
        {
            //todo: temporary.
            OverallDifficulty = AverageNoteDensity * 3.25f;
        }
    }
}
