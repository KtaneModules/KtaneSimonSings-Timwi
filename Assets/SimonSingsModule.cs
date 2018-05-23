using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

    public KMSelectable[] Keys;
    public MeshRenderer CentralLed;
    public MeshRenderer[] StatusLeds;
    public Material StatusLitMaterial;
    public Material StatusUnlitMaterial;

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private int _curStage;
    private int _subprogress;
    private List<int> _keysToPress;
    private int[] _flashingColors;
    private int _firstNumber;
    private int _secondNumber;
    private bool _hasVowel;
    private bool _isSolved;
    private Color[] _keyColors;

    private static readonly string[] _keyNames = @"C,C#,D,D#,E,F,F#,G,G#,A,A#,B".Split(',');

    void Start()
    {
        _moduleId = _moduleIdCounter++;
        _keyColors = new Color[12];

        // White keys
        assignKeyColors(new[] { 0, 2, 4, 5, 7, 9, 11 }, .9f, 1f, new float[] { 35, 288, 234, 339, 85, 131, 185 });
        // Black keys
        assignKeyColors(new[] { 1, 3, 6, 8, 10 }, .4f, .5f, new float[] { 359, 46, 120, 311, 175 });

        for (int i = 0; i < Keys.Length; i++)
            Keys[i].OnInteract = getKeyPressHandler(i);

        _hasVowel = Bomb.GetSerialNumberLetters().Any(ch => "AEIOU".Contains(ch));
        Debug.LogFormat(@"[Simon Sings #{0}] Serial number {1} a vowel, so start on the {2}.", _moduleId, _hasVowel ? "contains" : "does not contain", _hasVowel ? "left" : "right");

        _keysToPress = new List<int>();
        initStage(0);
        StartCoroutine(flashing());
    }

    private void assignKeyColors(int[] keyIndices, float minLightness, float maxLightness, float[] defaultHues)
    {
        var numTotalTries = 0;
        var hues = new List<float>();

        tryEverythingAgain:
        numTotalTries++;
        hues.Clear();
        if (numTotalTries > 1000)
        {
            // If we can’t find a color combination after 1000 attempts,
            // give up and use the defaults. This should happen very rarely.
            hues.AddRange(defaultHues);
            goto done;
        }

        for (int i = 0; i < keyIndices.Length; i++)
        {
            var numTries = 0;

            tryAgain:
            numTries++;
            if (numTries > 10)
                goto tryEverythingAgain;
            var hue = Rnd.Range(0f, 360f);

            for (int j = 0; j < hues.Count; j++)
            {
                var ds = sphericalDistance(hue, 10, hues[j], 10);
                if (ds < .8f)
                    goto tryAgain;
            }

            hues.Add(hue);
        }

        done:
        for (int i = 0; i < keyIndices.Length; i++)
        {
            Debug.LogFormat(@"[Simon Sings #{0}] Hue for {1} is {2:0}.", _moduleId, _keyNames[keyIndices[i]], hues[i]);
            var color = Color.HSVToRGB(hues[i] / 360, Rnd.Range(.6f, .8f), Rnd.Range(minLightness, maxLightness));
            Keys[keyIndices[i]].GetComponent<MeshRenderer>().material.color = color;
            Keys[keyIndices[i] + 12].GetComponent<MeshRenderer>().material.color = color;
            _keyColors[keyIndices[i]] = color;
        }
    }

    private static double sphericalDistance(float long1, float lat1, float long2, float lat2)
    {
        var dl = Mathf.Abs(lat1 - lat2);
        var c1 = Mathf.Cos(long1 * Mathf.PI / 180);
        var c2 = Mathf.Cos(long2 * Mathf.PI / 180);
        var s1 = Mathf.Sin(long1 * Mathf.PI / 180);
        var s2 = Mathf.Sin(long2 * Mathf.PI / 180);
        var ds = Mathf.Atan2(Mathf.Sqrt(sqr(c2 * Mathf.Sin(dl)) + sqr(c1 * s2 - s1 * c2 * Mathf.Cos(dl))), s1 * s2 + c1 * c2 * Mathf.Cos(dl));
        return ds;
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
            if (_isSolved)
                return false;

            if (i != _keysToPress[_subprogress])
            {
                Debug.LogFormat(@"[Simon Sings #{0}] Expected: {1}, pressed: {2}. Strike!", _moduleId, keyName(_keysToPress[_subprogress]), keyName(i));
                _subprogress = 0;
                Module.HandleStrike();
            }
            else
            {
                Debug.LogFormat(@"[Simon Sings #{0}] Pressed: {1}. Correct!", _moduleId, keyName(i));
                _subprogress++;
                if (_subprogress == 2 * (_curStage + 1))
                    initStage(_curStage + 1);
            }

            return false;
        };
    }

    private string keyName(int key)
    {
        return string.Format("{0} {1}", key >= 12 ? "right" : "left", _keyNames[key % 12]);
    }

    void initStage(int stage)
    {
        _curStage = stage;
        for (int i = 0; i < StatusLeds.Length; i++)
            StatusLeds[i].material = i < stage ? StatusLitMaterial : StatusUnlitMaterial;
        if (stage == 3)
        {
            Debug.LogFormat(@"[Simon Sings #{0}] Module solved.", _moduleId);
            Module.HandlePass();
            _isSolved = true;
        }
        else
        {
            var prevFlashingColors = _flashingColors;
            var prevFirst = _firstNumber;
            var prevSecond = _secondNumber;

            var keys = Enumerable.Range(0, 12).ToList().Shuffle();
            _flashingColors = keys.Take(8).ToArray();

            // Prevent a D from appearing right after a B (special case that would be in conflict)
            var dPos = Array.IndexOf(_flashingColors, 2);
            if (dPos > 0 && _flashingColors[dPos - 1] == 11)
            {
                _flashingColors[dPos - 1] = 2;
                _flashingColors[dPos] = 11;
            }

            var bits = new List<bool>();
            for (int i = 0; i < _flashingColors.Length; i++)
            {
                switch (_flashingColors[i])
                {
                    case 0: // C
                        bits.Add(i == 0 || i == 3 || i == 4 || i == 7);
                        break;
                    case 1: // C#
                        bits.Add(i == 1 || i == 2 || i == 5 || i == 6);
                        break;
                    case 2: // D
                        bits.Add(i == 0 ? Bomb.GetSerialNumberNumbers().Last() % 2 != 0 : !bits.Last());
                        break;
                    case 3: // D#
                        bits.Add(i % 4 == Bomb.GetPortPlateCount() - 1);
                        break;
                    case 4: // E
                        bits.Add(Bomb.GetPortPlateCount() == 0 ? Bomb.GetBatteryCount() % 2 != 0 : i % 4 == Bomb.GetPortPlates().Max(pp => pp.Length) - 1);
                        break;
                    case 5: // F
                        bits.Add(_curStage == 2);
                        break;
                    case 6: // F#
                        bits.Add(_curStage == Bomb.GetSerialNumberLetters().Count() - 2);
                        break;
                    case 7: // G
                        bits.Add(i == 0 || i == 4 ? Bomb.GetIndicators().Count() % 2 != 0 : new[] { 1, 3, 6, 8, 10 }.Contains(_flashingColors[4 * (i / 4)]));
                        break;
                    case 8: // G#
                        bits.Add(_curStage == 0 ? Bomb.GetPortCount() % 2 != 0 : new[] { 1, 3, 6, 8, 10 }.Any(key => prevFlashingColors.Contains(key)));
                        break;
                    case 9: // A
                        bits.Add(_curStage == 0 ? Bomb.GetOnIndicators().Count() % 2 == Bomb.GetOffIndicators().Count() % 2 : prevFirst < 2 || prevSecond < 2);
                        break;
                    case 10:    // A#
                        bits.Add(Enumerable.Range(4 * (i / 4), 4).Any(n => n % 4 != i % 4 && (_flashingColors[n] == 5 || _flashingColors[n] == 6)));
                        break;
                    case 11:    // B
                        bits.Add(false);
                        break;
                }
            }

            for (int i = 0; i < _flashingColors.Length; i++)
            {
                if (_flashingColors[i] == 11)
                {
                    var curNumber = bits.Skip(4 * (i / 4)).Take(4).Aggregate(0, (p, n) => (p << 1) | (n ? 1 : 0));
                    var newNumber = curNumber | (1 << (3 - i % 4));
                    if (new[] { 2, 3, 5, 7, 11, 13 }.Contains(newNumber))
                        bits[i] = true;
                }
            }

            _firstNumber = bits.Take(4).Aggregate(0, (p, n) => (p << 1) | (n ? 1 : 0));
            _secondNumber = bits.Skip(4).Take(4).Aggregate(0, (p, n) => (p << 1) | (n ? 1 : 0));

            _keysToPress.Add((_firstNumber < 12 ? _firstNumber : _flashingColors[_firstNumber - 12]) + (_hasVowel ? 0 : 12));
            _keysToPress.Add((_secondNumber < 12 ? _secondNumber : _flashingColors[_secondNumber - 12 + 4]) + (_hasVowel ? 12 : 0));

            _subprogress = 0;

            Debug.LogFormat(@"[Simon Sings #{0}] Stage {1} flashing colors correspond to keys: {2}", _moduleId, stage + 1, _flashingColors.Select(col => _keyNames[col]).JoinString(", "));
            Debug.LogFormat(@"[Simon Sings #{0}] Stage {1} solution: {2}", _moduleId, stage + 1, Enumerable.Range(0, 2 * (stage + 1)).Select(k => keyName(_keysToPress[k])).JoinString(", "));
        }
    }

    private IEnumerator flashing()
    {
        while (!_isSolved)
        {
            for (int i = 0; i < _flashingColors.Length; i++)
            {
                CentralLed.material.color = _keyColors[_flashingColors[i]];
                yield return new WaitForSeconds(.7f);
                CentralLed.material.color = new Color(0x27 / 255f, 0x22 / 255f, 0x1e / 255f);
                yield return new WaitForSeconds(.1f);
            }

            yield return new WaitForSeconds(1.2f);
        }
    }

#pragma warning disable 0414
    private readonly string TwitchHelpMessage = "Play your answer with “!{0} play left G# right E”. (Keys are C, C#, D, D#, E, F, F#, G, G#, A, A#, B.)";
#pragma warning restore 0414

    private IEnumerable<KMSelectable> ProcessTwitchCommand(string command)
    {
        var keys = new List<KMSelectable>();
        var match = Regex.Match(command.Trim().ToUpperInvariant(),
            "^(?:press|play|submit) ((?: *(?:left|right|l|r) ?(?:C#?|D[b#]?|Eb?|F#?|G[b#]?|A[b#]?|Bb?),?)+)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        if (!match.Success)
            return null;

        var pieces = match.Groups[1].Value.Trim()
            .Replace("DB", "C#").Replace("EB", "D#").Replace("GB", "F#").Replace("AB", "G#").Replace("BB", "A#")
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        var left = false;
        for (int i = 0; i < pieces.Length; i++)
        {
            if (pieces[i].StartsWith("L"))
                left = true;
            if (pieces[i].StartsWith("R"))
                left = false;
            for (int j = 0; j < _keyNames.Length; j++)
                if (pieces[i].EndsWith(_keyNames[j]) || pieces[i].EndsWith(_keyNames[j] + ","))
                    keys.Add(Keys[j + (left ? 0 : 12)]);
        }

        return keys;
    }
}
