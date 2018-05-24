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
    private static readonly int[] _whiteKeys = new[] { 0, 2, 4, 5, 7, 9, 11 };
    private static readonly int[] _blackKeys = new[] { 1, 3, 6, 8, 10 };
    private static readonly int[] _primes = new[] { 2, 3, 5, 7, 11, 13 };

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        const float minDist = .55f;

        var colors = Enumerable.Range(0, 12).Select(_ => new Color(Rnd.Range(0, 1f), Rnd.Range(0, 1f), Rnd.Range(0, 1f))).ToArray();

        const int iterations = 12;
        for (int iter = 0; iter < iterations; iter++)
        {
            var deltas = new Color[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    var d = colorDist(colors[i], colors[j]);
                    if (d < minDist)
                    {
                        var val = normalize(colors[i] - colors[j]) * (minDist - d) * (1 + (colors[i].r * colors[j].r * .3f)) * (1 + (colors[i].g * colors[j].g * .6f)) * (1 + (colors[i].b * colors[j].b * .1f));
                        if (d == 0)
                        {
                            val = new Color(Rnd.Range(0, 1f), Rnd.Range(0, 1f), Rnd.Range(0, 1f));
                            iter = 0;
                        }
                        deltas[i] += val;
                        deltas[j] -= val;
                    }
                }
            }
            for (int i = 0; i < colors.Length; i++)
            {
                var nPt = colors[i] + deltas[i];
                colors[i] = new Color(clip(nPt.r), clip(nPt.g), clip(nPt.b));
                if (colors[i] == new Color(0, 0, 0) || colors[i] == new Color(1, 1, 1))
                {
                    colors[i] = new Color(Rnd.Range(0, 1f), Rnd.Range(0, 1f), Rnd.Range(0, 1f));
                    iter = 0;
                }
            }
        }

        for (int i = 0; i < colors.Length; i++)
        {
            float h, s, v;
            Color.RGBToHSV(colors[i], out h, out s, out v);
            v = (v * .8f) + .2f;
            colors[i] = Color.HSVToRGB(h, s, v);
        }

        var sorted = colors.OrderBy(c => c.r * .3f + c.g * .6f + c.b * .1f).ToArray();
        var blackColors = sorted.Subarray(0, 5).Shuffle();
        var whiteColors = sorted.Subarray(5, 7).Shuffle();

        Debug.LogFormat(@"<Simon Sings #{0}> White key colors: {1}.", _moduleId, whiteColors.JoinString(", "));
        Debug.LogFormat(@"<Simon Sings #{0}> Black key colors: {1}.", _moduleId, blackColors.JoinString(", "));

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
        initStage(0, 0);
        StartCoroutine(flashing());
    }

    private static Color normalize(Color c)
    {
        var d = Mathf.Sqrt(sqr(c.r) + sqr(c.g) + sqr(c.b));
        if (d == 0)
            return new Color(0, 0, 0);
        return new Color(c.r / d, c.g / d, c.b / d);
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
                    initStage(_curStage + 1, i);
            }

            return false;
        };
    }

    private string keyName(int key)
    {
        return string.Format("{0} {1}", key >= 12 ? "right" : "left", _keyNames[key % 12]);
    }

    void initStage(int stage, int lastPressed)
    {
        _curStage = stage;
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
                    case 1: // C♯/D♭
                        bits.Add(i == 1 || i == 2 || i == 5 || i == 6);
                        break;
                    case 2: // D
                        bits.Add(i == 0 ? Bomb.GetSerialNumberNumbers().Last() % 2 != 0 : !bits.Last());
                        break;
                    case 3: // D♯/E♭
                        bits.Add(i % 4 == Bomb.GetPortPlateCount() - 1);
                        break;
                    case 4: // E
                        bits.Add(Bomb.GetPortPlateCount() == 0 ? Bomb.GetBatteryCount() % 2 != 0 : i % 4 == Bomb.GetPortPlates().Max(pp => pp.Length) - 1);
                        break;
                    case 5: // F
                        bits.Add(_curStage == 2);
                        break;
                    case 6: // F♯/G♭
                        bits.Add(_curStage == Bomb.GetSerialNumberLetters().Count() - 2);
                        break;
                    case 7: // G
                        bits.Add(i == 0 || i == 4 ? Bomb.GetIndicators().Count() % 2 != 0 : _blackKeys.Contains(_flashingColors[4 * (i / 4)]));
                        break;
                    case 8: // G♯/A♭
                        bits.Add(_curStage == 0 ? Bomb.GetPortCount() % 2 != 0 : Enumerable.Range(0, 7).Any(ix => _blackKeys.Contains(prevFlashingColors[ix]) && _blackKeys.Contains(prevFlashingColors[ix + 1])));
                        break;
                    case 9: // A
                        bits.Add(_curStage == 0 ? Bomb.GetOnIndicators().Count() % 2 == Bomb.GetOffIndicators().Count() % 2 : prevFirst < 5 || prevSecond < 5);
                        break;
                    case 10:    // A♯/B♭
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
                    if (_primes.Contains(newNumber))
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

    private IEnumerator solveAnimation(int startAt)
    {
        for (int i = 0; i < 12; i++)
        {
            yield return new WaitForSeconds(.05f);
            for (int j = 0; j < 24; j++)
                Keys[(i + j) % 24].GetComponent<MeshRenderer>().material.color = _keyColors[j % 12];
        }

        var black = new Color(0x26 / 255f, 0x26 / 255f, 0x26 / 255f);
        var white = new Color(0xD0 / 255f, 0xD0 / 255f, 0xD0 / 255f);
        for (int i = 0; i < 12; i++)
        {
            yield return new WaitForSeconds(.025f);
            Keys[i + startAt].GetComponent<MeshRenderer>().material.color = _blackKeys.Contains((i + startAt) % 12) ? white : black;
            Keys[(23 - i + startAt) % 24].GetComponent<MeshRenderer>().material.color = _blackKeys.Contains((23 - i + startAt) % 12) ? white : black;
        }
        for (int i = 11; i >= 0; i--)
        {
            yield return new WaitForSeconds(.025f);
            Keys[i + startAt].GetComponent<MeshRenderer>().material.color = _blackKeys.Contains((i + startAt) % 12) ? black : white;
            Keys[(23 - i + startAt) % 24].GetComponent<MeshRenderer>().material.color = _blackKeys.Contains((23 - i + startAt) % 12) ? black : white;
        }
    }

    private IEnumerator flashing()
    {
        while (true)
        {
            for (int i = 0; i < _flashingColors.Length; i++)
            {
                CentralLed.material.color = _keyColors[_flashingColors[i]];
                yield return new WaitForSeconds(1.2f);
                CentralLed.material.color = new Color(0x1e / 255f, 0x1a / 255f, 0x17 / 255f);
                yield return new WaitForSeconds(.1f);
                if (_isSolved)
                    yield break;
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
            "^(?:press |play |submit |sing |)((?:(?:left|right|l|r)[ ,;]?(?:C#?|D[b#]?|Eb?|F#?|G[b#]?|A[b#]?|Bb?)[ ,;]*)+)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        if (!match.Success)
            return null;

        var pieces = match.Groups[1].Value.Trim()
            .Replace("DB", "C#").Replace("EB", "D#").Replace("GB", "F#").Replace("AB", "G#").Replace("BB", "A#")
            .Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var left = false;

        foreach (var piece in pieces)
        {
            left |= piece.StartsWith("L");
            left &= !piece.StartsWith("R");

            for (var j = 0; j < _keyNames.Length; j++)
                if (piece.EndsWith(_keyNames[j]))
                    keys.Add(Keys[j + (left ? 0 : 12)]);
        }

        return keys;
    }
}
