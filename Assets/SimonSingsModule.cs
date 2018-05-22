using System.Collections;
using System.Linq;
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
    private int[] _keysToPress;
    private int[] _flashingColors;
    private bool _isSolved;

    private static readonly string[] _keyNames = @"C,C#,D,D#,E,F,F#,G,G#,A,A#,B".Split(',');

    void Start()
    {
        _moduleId = _moduleIdCounter++;
        _keysToPress = new int[6];
        for (int i = 0; i < 6; i++)
            _keysToPress[i] = Rnd.Range(0, 12);

        var hasVowel = Bomb.GetSerialNumberLetters().Any(ch => "AEIOU".Contains(ch));
        Debug.LogFormat(@"[Simon Sings #{0}] Serial number {1} a vowel, so start on the {2}.", _moduleId, hasVowel ? "contains" : "does not contain", hasVowel ? "left" : "right");
        for (int i = 0; i < 3; i++)
            _keysToPress[2 * i + (hasVowel ? 1 : 0)] += 12;

        for (int i = 0; i < Keys.Length; i++)
            Keys[i].OnInteract = getKeyPressHandler(i);

        initStage(0);
        StartCoroutine(flashing());
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
            _subprogress = 0;
            _flashingColors = Enumerable.Range(0, 8).Select(i => ((_keysToPress[2 * stage + i / 4] % 12) & (1 << (3 - i % 4))) == 0 ? 0 : 1).ToArray();

            Debug.LogFormat(@"[Simon Sings #{0}] Stage {1} flashing colors: {2}", _moduleId, stage + 1, _flashingColors.JoinString(", "));
            Debug.LogFormat(@"[Simon Sings #{0}] Stage {1} solution: {2}", _moduleId, stage + 1, Enumerable.Range(0, 2 * (stage + 1)).Select(k => keyName(_keysToPress[k])).JoinString(", "));
        }
    }

    private IEnumerator flashing()
    {
        while (!_isSolved)
        {
            for (int i = 0; i < _flashingColors.Length; i++)
            {
                CentralLed.material.color = new Color(0, 0, _flashingColors[i]);
                yield return new WaitForSeconds(.7f);
                CentralLed.material.color = new Color(1, 1, 1);
                yield return new WaitForSeconds(.1f);
            }

            CentralLed.material.color = new Color(1, 1, 1);
            yield return new WaitForSeconds(1.2f);
        }
    }
}
