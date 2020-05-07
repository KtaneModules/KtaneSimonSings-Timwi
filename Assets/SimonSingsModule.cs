using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using KModkit;
using SimonSings;
using UnityEngine;
using Rnd = UnityEngine.Random;

/// <summary>
/// On the Subject of Simon Sings
/// Created by MarioXMan and Timwi
/// </summary>
public class SimonSingsModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMAudio Audio;
    public KMRuleSeedable RuleSeedable;

    public KMSelectable[] Keys;
    public MeshRenderer CentralLed;
    public KMSelectable CentralSelectable;
    public MeshRenderer[] StatusLeds;
    public Material StatusLitMaterial;
    public Material StatusUnlitMaterial;

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private int _curStage;
    private int _subprogress;
    private List<int> _keysToPress;
    private int[][] _flashingColors;
    private int[] _firstNumber;
    private int[] _secondNumber;
    private bool _hasVowel;
    private bool _isSolved;
    private Color[] _keyColors;
    private float _startedHolding;
    private Coroutine _holding = null;
    private bool _animationDone;

    private static readonly Color[] _whiteKeyColors = {
        new Color(204/255f, 210/255f, 213/255f),
        new Color(227/255f, 54/255f, 54/255f),
        new Color(60/255f, 197/255f, 60/255f),
        new Color(75/255f, 75/255f, 255/255f),
        new Color(212/255f, 212/255f, 54/255f),
        new Color(219/255f, 60/255f, 184/255f),
        new Color(0/255f, 204/255f, 228/255f),
        new Color(227/255f, 155/255f, 47/255f),
        new Color(221/255f, 197/255f, 154/255f),
        new Color(163/255f, 246/255f, 58/255f),
        new Color(118/255f, 144/255f, 255/255f),
        new Color(248/255f, 176/255f, 197/255f),
        new Color(129/255f, 227/255f, 172/255f),
        new Color(188/255f, 148/255f, 255/255f)
    };

    private static readonly Color[] _blackKeyColors = {
        new Color(18/255f, 18/255f, 146/255f),
        new Color(0/255f, 102/255f, 102/255f),
        new Color(152/255f, 92/255f, 25/255f),
        new Color(114/255f, 2/255f, 2/255f),
        new Color(83/255f, 19/255f, 83/255f),
        new Color(18/255f, 74/255f, 18/255f),
        new Color(69/255f, 69/255f, 69/255f),
        new Color(125/255f, 125/255f, 16/255f)
    };

    private static readonly string[] _keyNames = @"C,C♯/D♭,D,D♯/E♭,E,F,F♯/G♭,G,G♯/A♭,A,A♯/B♭,B".Split(',');
    private static readonly string[] _tpKeyNames = @"C,C#,D,D#,E,F,F#,G,G#,A,A#,B".Split(',');
    private static readonly int[] _whiteKeys = new[] { 0, 2, 4, 5, 7, 9, 11 };
    private static readonly int[] _blackKeys = new[] { 1, 3, 6, 8, 10 };
    private static readonly int[] _primes = new[] { 2, 3, 5, 7, 11, 13 };

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        var whiteColors = _whiteKeyColors.ToList().Shuffle().Take(7).ToArray();
        var blackColors = _blackKeyColors.ToList().Shuffle().Take(5).ToArray();

        Debug.LogFormat(@"<Simon Sings #{0}> White key colors: {1}.", _moduleId, whiteColors.Join(", "));
        Debug.LogFormat(@"<Simon Sings #{0}> Black key colors: {1}.", _moduleId, blackColors.Join(", "));

        _keyColors = new Color[12];
        for (int i = 0; i < _whiteKeys.Length; i++)
            _keyColors[_whiteKeys[i]] = whiteColors[i];
        for (int i = 0; i < _blackKeys.Length; i++)
            _keyColors[_blackKeys[i]] = blackColors[i];

        for (int i = 0; i < _keyColors.Length; i++)
        {
            Keys[i].GetComponent<MeshRenderer>().material.color = _keyColors[i];
            Keys[i + 12].GetComponent<MeshRenderer>().material.color = _keyColors[i];
        }

        for (int i = 0; i < Keys.Length; i++)
            Keys[i].OnInteract = getKeyPressHandler(i);

        _hasVowel = Bomb.GetSerialNumberLetters().Any(ch => "AEIOU".Contains(ch));
        Debug.LogFormat(@"[Simon Sings #{0}] Serial number {1} a vowel, so start on the {2}.", _moduleId, _hasVowel ? "contains" : "does not contain", _hasVowel ? "left" : "right");

        _keysToPress = new List<int>();
        generatePuzzle();
        initStage(0, 0);
        StartCoroutine(flashing());

        CentralSelectable.OnInteract = startHolding;
        CentralSelectable.OnInteractEnded = endHolding;
    }

    private bool startHolding()
    {
        if (!_isSolved)
            _holding = StartCoroutine(holding(Time.time));
        return false;
    }

    private void endHolding()
    {
        if (_holding != null)
            StopCoroutine(_holding);
        _holding = null;
    }

    private IEnumerator holding(float time)
    {
        _startedHolding = time;
        yield return new WaitForSeconds(.5f);
        if (_holding != null && _startedHolding == time && !_isSolved)
        {
            if (_curStage == 0)
                Debug.LogFormat(@"[Simon Sings #{0}] Module NOT reset because we’re in Stage 1.", _moduleId);
            else
            {
                Debug.LogFormat(@"[Simon Sings #{0}] MODULE RESET on request.", _moduleId);
                StartCoroutine(playResetSound(_curStage));
                initStage(0, 0);
                Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonRelease, CentralSelectable.transform);
            }
        }
    }

    private IEnumerator playResetSound(int oldStage)
    {
        for (int i = oldStage * 5; i >= 0; i--)
        {
            Audio.PlaySoundAtTransform((i % 2 == 0 ? "Aah" : "Ooh") + i, CentralSelectable.transform);
            yield return new WaitForSeconds(.1f);
        }
    }

    private static Color normalize(Color c)
    {
        var d = Mathf.Sqrt(sqr(c.r) + sqr(c.g) + sqr(c.b));
        return d == 0 ? new Color(0, 0, 0) : new Color(c.r / d, c.g / d, c.b / d);
    }

    private static float clip(float v)
    {
        return v < 0 ? 0 : v > 1 ? 1 : v;
    }

    private static float colorDist(Color c1, Color c2)
    {
        return Mathf.Sqrt((sqr(c1.r - c2.r)) + (sqr(c1.g - c2.g)) + (sqr(c1.b - c2.b)));
    }

    private static float sqr(float value)
    {
        return value * value;
    }

    private KMSelectable.OnInteractHandler getKeyPressHandler(int i)
    {
        return delegate
        {
            Keys[i].AddInteractionPunch(.5f);
            Audio.PlaySoundAtTransform((i < 12 ? "Aah" : "Ooh") + (i % 12), Keys[i].transform);
            if (_isSolved)
                return false;

            if (i != _keysToPress[_subprogress])
            {
                Debug.LogFormat(@"[Simon Sings #{0}] Expected: {1}, pressed: {2}. Strike! Back to start of stage {3}.", _moduleId, keyName(_keysToPress[_subprogress]), keyName(i), _curStage + 1);
                _subprogress = 0;
                Module.HandleStrike();
            }
            else
            {
                Debug.LogFormat(@"[Simon Sings #{0}] Pressed: {1}. Correct!", _moduleId, keyName(i));
                _subprogress++;
                if (_subprogress == 2 * (_curStage + 1))
                    initStage(_curStage + 1, i);
            }

            return false;
        };
    }

    private string keyName(int key)
    {
        return string.Format("{0} {1}", key >= 12 ? "right" : "left", _keyNames[key % 12]);
    }

    delegate TResult Func<T1, T2, T3, T4, T5, T6, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);

    private class RuleInfo
    {
        public string Name;

        // Arguments: 
        //  (1) (int[]) keys whose colors flashed; 
        //  (2) (int[]) keys whose colors flashed in the PREVIOUS stage; 
        //  (3) (int[]) values of the 4-digit numbers in the PREVIOUS stage;
        //  (4) (List<bool>) previous digit values in THIS stage; 
        //  (5) (int) index of current digit; 
        //  (6) (int) current stage
        public Func<int[], int[], int[], List<bool>, int, int, bool> Evaluate;

        public bool UsesPreviousDigit;
        public bool IsPrimeNumberRule;
    }

    private class FallbackInfo
    {
        public string Name;
        public Func<bool> Evaluate;
    }

    private class ElementInfo
    {
        public string Name;
        public Func<int> Evaluate;
    }

    static T[] newArray<T>(params T[] array) { return array; }

    void generatePuzzle()
    {
        var rnd = RuleSeedable.GetRNG();
        Debug.LogFormat(@"[Simon Sings #{0}] Using rule seed: {1}", _moduleId, rnd.Seed);

        // Generate the rule-seeded rules first
        var candidateFallbacks = rnd.ShuffleFisherYates(newArray
        (
            new FallbackInfo { Name = "There is an odd number of batteries", Evaluate = () => Bomb.GetBatteryCount() % 2 == 1 },
            new FallbackInfo { Name = "There is an odd number of indicators", Evaluate = () => Bomb.GetIndicators().Count() % 2 == 1 },
            new FallbackInfo { Name = "There is an odd number of ports", Evaluate = () => Bomb.GetPortCount() % 2 == 1 },
            new FallbackInfo { Name = "There is an odd number of letters in the serial number", Evaluate = () => Bomb.GetSerialNumberLetters().Count() % 2 == 1 },
            new FallbackInfo { Name = "There is an odd number of port plates", Evaluate = () => Bomb.GetPortPlateCount() % 2 == 1 },
            new FallbackInfo { Name = "The first digit of the serial number is odd", Evaluate = () => Bomb.GetSerialNumberNumbers().First() % 2 == 1 },
            new FallbackInfo { Name = "There is an odd number of distinct port types", Evaluate = () => Bomb.CountUniquePorts() % 2 == 1 },
            new FallbackInfo { Name = "The last digit of the serial number is odd", Evaluate = () => Bomb.GetSerialNumberNumbers().Last() % 2 == 1 },
            new FallbackInfo { Name = "There is an odd number of lit indicators", Evaluate = () => Bomb.GetOnIndicators().Count() % 2 == 1 },
            new FallbackInfo { Name = "There is an odd number of unlit indicators", Evaluate = () => Bomb.GetOffIndicators().Count() % 2 == 1 },
            new FallbackInfo { Name = "There is an odd number of battery holders", Evaluate = () => Bomb.GetBatteryHolderCount() % 2 == 1 },
            new FallbackInfo { Name = "There is an even number of batteries", Evaluate = () => Bomb.GetBatteryCount() % 2 == 0 },
            new FallbackInfo { Name = "The last digit of the serial number is even", Evaluate = () => Bomb.GetSerialNumberNumbers().Last() % 2 == 0 },
            new FallbackInfo { Name = "The first digit of the serial number is even", Evaluate = () => Bomb.GetSerialNumberNumbers().First() % 2 == 0 },
            new FallbackInfo { Name = "There is an even number of port plates", Evaluate = () => Bomb.GetPortPlateCount() % 2 == 0 },
            new FallbackInfo { Name = "There is an even number of indicators", Evaluate = () => Bomb.GetIndicators().Count() % 2 == 0 },
            new FallbackInfo { Name = "There is an even number of ports", Evaluate = () => Bomb.GetPortCount() % 2 == 0 },
            new FallbackInfo { Name = "There is an even number of distinct port types", Evaluate = () => Bomb.CountUniquePorts() % 2 == 0 },
            new FallbackInfo { Name = "There is an even number of letters in the serial number", Evaluate = () => Bomb.GetSerialNumberLetters().Count() % 2 == 0 },
            new FallbackInfo { Name = "There is an even number of lit indicators", Evaluate = () => Bomb.GetOnIndicators().Count() % 2 == 0 },
            new FallbackInfo { Name = "There is an even number of unlit indicators", Evaluate = () => Bomb.GetOffIndicators().Count() % 2 == 0 },
            new FallbackInfo { Name = "There is an even number of battery holders", Evaluate = () => Bomb.GetBatteryHolderCount() % 2 == 0 }
        ));

        int[] digitOrder = null;
        int[] keyOrder = null;
        var fallbackIx = 0;
        var elementIx = 0;
        var op = 0;
        ElementInfo[] candidateElements = null;

        var candidateRules = rnd.ShuffleFisherYates(newArray<Func<int, RuleInfo>>
        (
            digit =>
            {
                var stage = rnd.Next(0, 3);
                return new RuleInfo
                {
                    Name = string.Format("We are in the {0} stage of the module.", new[] { "first", "third", "second" }[stage]),
                    Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => st == new[] { 0, 2, 1 }[stage]
                };
            },

            digit =>
            {
                var fallback = candidateFallbacks[fallbackIx++];
                var flavour = rnd.Next(0, 2);
                return new RuleInfo
                {
                    Name = string.Format("If this is the {0} digit in its 4-digit binary number: {1}. Otherwise: This number’s {0} color referred to a {2} key.", new[] { "third", "second", "first", "fourth" }[digit], fallback.Name, new[] { "sharp/flat", "natural" }[flavour]),
                    Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => ix % 4 == new[] { 2, 1, 0, 3 }[digit] ? fallback.Evaluate() : _blackKeys.Contains(clrs[4 * (ix / 4) + new[] { 2, 1, 0, 3 }[digit]]) ? flavour == 0 : flavour == 1
                };
            },

            digit =>
            {
                var fallback = candidateFallbacks[fallbackIx++];
                var zeroOrOne = 1 - rnd.Next(0, 2);
                return new RuleInfo
                {
                    Name = string.Format("If this is the first of the 8 digits: {0}. Otherwise: The previous digit was {1}.", fallback.Name, zeroOrOne),
                    Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => ix == 0 ? fallback.Evaluate() : digitsCur[ix - 1] == (zeroOrOne != 0),
                    UsesPreviousDigit = true
                };
            },

            digit =>
            {
                var fallback = candidateFallbacks[fallbackIx++];
                return new RuleInfo
                {
                    Name = string.Format("If there are no indicators: {0}. Otherwise: The position of this digit in its 4-digit number matches the number of lit or unlit indicators, whichever is greater.", fallback.Name),
                    Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => Bomb.GetIndicators().Count() == 0 ? fallback.Evaluate() : (ix % 4 + 1) == Math.Max(Bomb.GetOnIndicators().Count(), Bomb.GetOffIndicators().Count())
                };
            },

            digit => new RuleInfo
            {
                Name = "The position of this digit in its 4-digit number matches the number of port plates.",
                Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => (ix % 4 + 1) == Bomb.GetPortPlateCount()
            },

            digit =>
            {
                var fallback = candidateFallbacks[fallbackIx++];
                var num = rnd.Next(3, 6);
                var flavour = rnd.Next(0, 2);
                return new RuleInfo
                {
                    Name = string.Format("If we are in the first stage: {0}. Otherwise: Exactly {1} colors flashing in the previous stage refer to {2} keys.", fallback.Name, num, new[] { "sharp/flat", "natural" }[flavour]),
                    Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => st == 0 ? fallback.Evaluate() : clrsPrev.Count(c => _blackKeys.Contains(c) ? flavour == 0 : flavour == 1) == num
                };
            },

            digit =>
            {
                var fallback = candidateFallbacks[fallbackIx++];
                return new RuleInfo
                {
                    Name = string.Format("If there are no port plates: {0}. Otherwise: The position of this digit in its 4-digit number matches the number of ports on the port plate with the most ports on it.", fallback.Name),
                    Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => Bomb.GetPortPlateCount() == 0 ? fallback.Evaluate() : (ix % 4 + 1) == Bomb.GetPortPlates().Max(plate => plate.Length)
                };
            },

            digit =>
            {
                var element = candidateElements[elementIx++];
                return new RuleInfo
                {
                    Name = string.Format("The current stage number matches the number of {0}.", element.Name),
                    Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => st + 1 == element.Evaluate()
                };
            },

            digit =>
            {
                var fallback = candidateFallbacks[fallbackIx++];
                return new RuleInfo
                {
                    Name = string.Format("If there are no battery holders: {0}. Otherwise: The position of this digit in its 4-digit number matches the number of batteries.", fallback.Name),
                    Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => Bomb.GetBatteryHolderCount() == 0 ? fallback.Evaluate() : (ix % 4 + 1) == Bomb.GetBatteryCount()
                };
            },

            digit => new RuleInfo
            {
                Name = string.Format("This is the {0} or {1} digit in its 4-digit binary number.", new[] { "first", "second", "third", "last" }[digitOrder[0]], new[] { "first", "second", "third", "last" }[digitOrder[1]]),
                Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => (ix % 4) == digitOrder[0] || (ix % 4) == digitOrder[1]
            },

            digit => new RuleInfo
            {
                Name = string.Format("Another color in this 4-digit number refers to {0} or {1}.", _keyNames[keyOrder[0]], _keyNames[keyOrder[1]]),
                Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => Enumerable.Range((ix / 4) * 4, 4).Any(i => i != ix && (clrs[i] == keyOrder[0] || clrs[i] == keyOrder[1]))
            },

            digit => new RuleInfo
            {
                Name = "This digit’s number would be a prime number if this digit is 1.",
                Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => _primes.Contains(Enumerable.Range((ix / 4) * 4, 4).Select(i => i == ix ? true : digitsCur[i]).Aggregate(0, (pr, nx) => (pr << 1) | (nx ? 1 : 0))),
                IsPrimeNumberRule = true
            },

            digit =>
            {
                var fallback = candidateFallbacks[fallbackIx++];
                return new RuleInfo
                {
                    Name = string.Format(
                        "If we are in the first stage: {0}. Otherwise: {1} of the 4-digit numbers in the previous stage {2} less than 5.",
                        fallback.Name,
                        new[] { "Neither", "One, but not both,", "One (or both)", "Both" }[op],
                        new[] { "were", "was", "was", "were" }[op]),
                    Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) =>
                    {
                        if (st == 0)
                            return fallback.Evaluate();
                        var c = valsPrev.Count(v => v < 5);
                        return op == 0 ? c == 0 : op == 1 ? c == 1 : op == 2 ? c > 0 : c == 2;
                    }
                };
            },

            digit =>
            {
                var stage = rnd.Next(0, 3);
                return new RuleInfo
                {
                    Name = string.Format("We are not in the {0} stage of the module.", new[] { "first", "third", "second" }[stage]),
                    Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => st != new[] { 0, 2, 1 }[stage]
                };
            },

            digit =>
            {
                var fallback = candidateFallbacks[fallbackIx++];
                var flavour = rnd.Next(0, 2);
                return new RuleInfo
                {
                    Name = string.Format("If we are in the first stage: {0}. Otherwise: Two colors flashing consecutively in the previous stage refer to {1} keys.", fallback.Name, new[] { "sharp/flat", "natural" }[flavour]),
                    Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) =>
                        st == 0 ? fallback.Evaluate() :
                        flavour == 0
                            ? Enumerable.Range(0, 7).Any(i => _blackKeys.Contains(clrsPrev[i]) && _blackKeys.Contains(clrsPrev[i + 1]))
                            : Enumerable.Range(0, 7).Any(i => !_blackKeys.Contains(clrsPrev[i]) && !_blackKeys.Contains(clrsPrev[i + 1]))
                };
            },

            digit => new RuleInfo
            {
                Name = string.Format("A color in the other 4-digit number refers to {0} or {1}.", _keyNames[keyOrder[2]], _keyNames[keyOrder[3]]),
                Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => Enumerable.Range((1 - ix / 4) * 4, 4).Any(i => clrs[i] == keyOrder[2] || clrs[i] == keyOrder[3])
            },

            digit =>
            {
                var flavour = rnd.Next(0, 2);
                return new RuleInfo
                {
                    Name = string.Format("The other number’s {0} color referred to a {1} key.", new[] { "third", "second", "first", "fourth" }[digit], new[] { "sharp/flat", "natural" }[flavour]),
                    Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => _blackKeys.Contains(clrs[(1 - ix / 4) * 4 + new[] { 2, 1, 0, 3 }[digit]]) ? flavour == 0 : flavour == 1
                };
            },

            digit => new RuleInfo
            {
                Name = string.Format("This is the {0} or {1} digit in its 4-digit binary number.", new[] { "first", "second", "third", "last" }[digitOrder[2]], new[] { "first", "second", "third", "last" }[digitOrder[3]]),
                Evaluate = (clrs, clrsPrev, valsPrev, digitsCur, ix, st) => (ix % 4) == digitOrder[2] || (ix % 4) == digitOrder[3]
            }
        ));

        candidateElements = rnd.ShuffleFisherYates(newArray(
            new ElementInfo { Name = "batteries", Evaluate = () => Bomb.GetBatteryCount() },
            new ElementInfo { Name = "battery holders", Evaluate = () => Bomb.GetBatteryHolderCount() },
            new ElementInfo { Name = "indicators", Evaluate = () => Bomb.GetIndicators().Count() },
            new ElementInfo { Name = "letters in the serial number minus one", Evaluate = () => Bomb.GetSerialNumberLetters().Count() - 1 },
            new ElementInfo { Name = "unlit indicators", Evaluate = () => Bomb.GetOffIndicators().Count() },
            new ElementInfo { Name = "ports", Evaluate = () => Bomb.GetPortCount() },
            new ElementInfo { Name = "distinct port types", Evaluate = () => Bomb.CountUniquePorts() },
            new ElementInfo { Name = "port plates", Evaluate = () => Bomb.GetPortPlateCount() },
            new ElementInfo { Name = "digits in the serial number minus one", Evaluate = () => Bomb.GetSerialNumberNumbers().Count() - 1 },
            new ElementInfo { Name = "lit indicators", Evaluate = () => Bomb.GetOnIndicators().Count() }
        ));

        digitOrder = rnd.ShuffleFisherYates(new[] { 3, 1, 0, 2 });
        keyOrder = rnd.ShuffleFisherYates(new[] { 0, 1, 6, 3, 4, 10, 2, 7, 8, 9, 5, 11 });
        op = rnd.Next(0, 4);

        var rules = new List<RuleInfo>();

        // Initialize the rules for the keys (rule seed)
        for (var i = 0; i < 12; i++)
        {
            rules.Add(candidateRules[i](rnd.Next(0, 4)));
            Debug.LogFormat(@"<Simon Sings #{0}> {1} = {2}", _moduleId, _keyNames[i], rules.Last().Name);
        }

        // There will be 3 stages
        _flashingColors = new int[3][];
        _firstNumber = new int[3];
        _secondNumber = new int[3];

        for (int stage = 0; stage < 3; stage++)
        {
            var keys = Enumerable.Range(0, 12).ToList().Shuffle();
            _flashingColors[stage] = keys.Take(8).ToArray();

            // Prevent a “previous digit” rule from appearing right after a “prime number” rule (special case that would be in conflict)
            for (int clrIx = 0; clrIx < 7; clrIx++)
            {
                if (rules[_flashingColors[stage][clrIx]].IsPrimeNumberRule && rules[_flashingColors[stage][clrIx + 1]].UsesPreviousDigit)
                {
                    var t = _flashingColors[stage][clrIx];
                    _flashingColors[stage][clrIx] = _flashingColors[stage][clrIx + 1];
                    _flashingColors[stage][clrIx + 1] = t;
                }
            }

            Debug.LogFormat(@"[Simon Sings #{0}] Stage {1} flashing: {2}", _moduleId, stage + 1, _flashingColors[stage].Select(col => _keyNames[col]).Join(", "));

            var bits = new List<bool>();
            for (int i = 0; i < _flashingColors[stage].Length; i++)
            {
                var rule = rules[_flashingColors[stage][i]];
                bits.Add(rule.IsPrimeNumberRule ? false : rule.Evaluate(_flashingColors[stage], stage == 0 ? null : _flashingColors[stage - 1], stage == 0 ? null : new[] { _firstNumber[stage - 1], _secondNumber[stage - 1] }, bits, i, stage));
            }

            for (int i = 0; i < _flashingColors[stage].Length; i++)
            {
                var rule = rules[_flashingColors[stage][i]];
                if (rule.IsPrimeNumberRule)
                    bits[i] = rule.Evaluate(_flashingColors[stage], stage == 0 ? null : _flashingColors[stage - 1], stage == 0 ? null : new[] { _firstNumber[stage - 1], _secondNumber[stage - 1] }, bits, i, stage);
            }

            _firstNumber[stage] = bits.Take(4).Aggregate(0, (p, n) => (p << 1) | (n ? 1 : 0));
            _secondNumber[stage] = bits.Skip(4).Take(4).Aggregate(0, (p, n) => (p << 1) | (n ? 1 : 0));

            _keysToPress.Add((_firstNumber[stage] < 12 ? _firstNumber[stage] : _flashingColors[stage][_firstNumber[stage] - 12]) + (_hasVowel ? 0 : 12));
            _keysToPress.Add((_secondNumber[stage] < 12 ? _secondNumber[stage] : _flashingColors[stage][_secondNumber[stage] - 12 + 4]) + (_hasVowel ? 12 : 0));

            Debug.LogFormat(@"[Simon Sings #{0}] Stage {1} digits: {2}", _moduleId, stage + 1, bits.Select(b => b ? "1" : "0").Join(", "));
            Debug.LogFormat(@"[Simon Sings #{0}] Stage {1} solution: {2}", _moduleId, stage + 1, Enumerable.Range(0, 2 * (stage + 1)).Select(k => keyName(_keysToPress[k])).Join(", "));
        }
    }

    void initStage(int stage, int lastPressed)
    {
        _curStage = stage;
        _subprogress = 0;
        for (int i = 0; i < StatusLeds.Length; i++)
            StatusLeds[i].material = i < stage ? StatusLitMaterial : StatusUnlitMaterial;
        if (stage == 3)
        {
            Debug.LogFormat(@"[Simon Sings #{0}] Module solved.", _moduleId);
            Module.HandlePass();
            _isSolved = true;
            StartCoroutine(solveAnimation(lastPressed));
        }
        else
            Debug.LogFormat(@"[Simon Sings #{0}] Start of stage {1}.", _moduleId, _curStage + 1);
    }

    private IEnumerator solveAnimation(int startAt)
    {
        yield return new WaitForSeconds(1f);
        Audio.PlaySoundAtTransform("Victory", transform);

        for (int i = 0; i < 12; i++)
        {
            yield return new WaitForSeconds(.06f);
            for (int j = 0; j < 24; j++)
                Keys[(i + j) % 24].GetComponent<MeshRenderer>().material.color = _keyColors[j % 12];
        }

        var black = new Color(0x26 / 255f, 0x26 / 255f, 0x26 / 255f);
        var white = new Color(0xD0 / 255f, 0xD0 / 255f, 0xD0 / 255f);
        for (int i = 0; i < 12; i++)
        {
            yield return new WaitForSeconds(.03f);
            Keys[(i + startAt) % 24].GetComponent<MeshRenderer>().material.color = _blackKeys.Contains((i + startAt) % 12) ? white : black;
            Keys[(23 - i + startAt) % 24].GetComponent<MeshRenderer>().material.color = _blackKeys.Contains((23 - i + startAt) % 12) ? white : black;
        }
        for (int i = 11; i >= 0; i--)
        {
            yield return new WaitForSeconds(.03f);
            Keys[(i + startAt) % 24].GetComponent<MeshRenderer>().material.color = _blackKeys.Contains((i + startAt) % 12) ? black : white;
            Keys[(23 - i + startAt) % 24].GetComponent<MeshRenderer>().material.color = _blackKeys.Contains((23 - i + startAt) % 12) ? black : white;
        }

        _animationDone = true;
    }

    private IEnumerator flashing()
    {
        while (_curStage < 3)
        {
            for (int i = 0; _curStage < 3 && i < _flashingColors[_curStage].Length; i++)
            {
                CentralLed.material.color = _keyColors[_flashingColors[_curStage][i]];
                yield return new WaitForSeconds(1.5f);
                CentralLed.material.color = new Color(0x1e / 255f, 0x1a / 255f, 0x17 / 255f);
                yield return new WaitForSeconds(.1f);
                if (_isSolved)
                    yield break;
            }

            yield return new WaitForSeconds(1.2f);
        }
    }

    private Color PickRandomFrom(List<Color> list)
    {
        var ix = Rnd.Range(0, list.Count);
        var result = list[ix];
        list.RemoveAt(ix);
        return result;
    }

#pragma warning disable 0414
    private readonly string TwitchHelpMessage = "!{0} play left G# right E [keys are C, C#, D, D#, E, F, F#, G, G#, A, A#, B] | !{0} reset";
#pragma warning restore 0414

    private IEnumerator ProcessTwitchCommand(string command)
    {
        if (command.Trim().ToLowerInvariant() == "reset")
        {
            yield return null;
            CentralSelectable.OnInteract();
            yield return new WaitForSeconds(.6f);
            CentralSelectable.OnInteractEnded();
        }

        var keys = new List<KMSelectable>();
        var match = Regex.Match(command.Trim().ToUpperInvariant(),
            "^(?:press |play |submit |sing |)((?:(?:left|right|l|r)[ ,;]?(?:C#?|D[b#]?|Eb?|F#?|G[b#]?|A[b#]?|Bb?)[ ,;]*)+)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        if (!match.Success)
            yield break;

        var pieces = match.Groups[1].Value.Trim()
            .Replace("DB", "C#").Replace("EB", "D#").Replace("GB", "F#").Replace("AB", "G#").Replace("BB", "A#")
            .Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var left = false;

        foreach (var piece in pieces)
        {
            left |= piece.StartsWith("L");
            left &= !piece.StartsWith("R");

            for (var j = 0; j < _tpKeyNames.Length; j++)
                if (piece.EndsWith(_tpKeyNames[j]))
                    keys.Add(Keys[j + (left ? 0 : 12)]);
        }

        yield return null;
        foreach (var key in keys)
        {
            key.OnInteract();
            yield return new WaitForSeconds(.4f);
        }
    }

    IEnumerator TwitchHandleForcedSolve()
    {
        for (var stage = _curStage; stage < 3; stage++)
        {
            for (var i = _subprogress; i < 2 * (stage + 1); i++)
            {
                Keys[_keysToPress[i]].OnInteract();
                yield return new WaitForSeconds(.25f);
            }
            yield return new WaitForSeconds(.25f);
        }

        while (!_animationDone)
            yield return true;
    }
}
