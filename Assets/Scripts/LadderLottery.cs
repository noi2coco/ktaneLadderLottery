using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KModkit;
using Rnd = UnityEngine.Random;
using System.Text.RegularExpressions;

public delegate bool IsActive(LadderLottery module);

public class Link
{
    public Link(IsActive active, Column c)
    {
        isActive = active;
        segment = new Segment(c);
    }
    public enum Point
    {
        A, B, C, D
    }
    public enum Column
    {
        One, Two, Three
    }
    public class Segment
    {
        public Segment(Column column)
        {
            switch (column)
            {
                case Column.One:
                    pt1 = Point.A;
                    pt2 = Point.B;
                    break;
                case Column.Two:
                    pt1 = Point.B;
                    pt2 = Point.C;
                    break;
                case Column.Three:
                    pt1 = Point.C;
                    pt2 = Point.D;
                    break;
            }
        }
        public Point pt1, pt2;
    }
    public bool contains(Point pt)
    {
        return pt == segment.pt1 || pt == segment.pt2;
    }
    public Point getOther(Point pt)
    {
        return pt == segment.pt1 ? segment.pt2 : segment.pt1;
    }
    Segment segment;
    public IsActive isActive;
}

public class LadderLottery : MonoBehaviour
{
    public KMBombModule Module;
    public KMBombInfo Bomb;
    public KMAudio Audio;
    public KMRuleSeedable RuleSeedable;
    public Material MatWireOn;
    public Material MatWireOff;
    public Material MatWireStrike;
    public KMSelectable[] Buttons;
    public MeshRenderer[] TopWires;
    public MeshRenderer[] DownWires;

    private float _initialTime;
    private static int _moduleIdCounter = 0;
    private int _moduleId;
    private bool _isSolved;
    private bool _isCoroutineActive;
    private static T[] newArray<T>(params T[] array) { return array; }
    private List<Link> _links;
    private List<IsActive> _rules = new List<IsActive>{
        delegate (LadderLottery mod) { return Char.IsLetter(mod.Bomb.GetSerialNumber().First()); },
        delegate (LadderLottery mod) { return mod.Bomb.GetPortPlates().Any(c => c.Contains(Port.Parallel.ToString()) && c.Contains(Port.Serial.ToString())); },
        delegate (LadderLottery mod) { return mod.Bomb.GetSerialNumberNumbers().Sum() > 8; },
        delegate (LadderLottery mod) { return mod.Bomb.GetStrikes() <= 1; },
        delegate (LadderLottery mod) { return mod.Bomb.GetPortPlates().Any(c => c.Length == 0); },
        delegate (LadderLottery mod) { return mod.Bomb.GetBatteryCount(Battery.D) % 2 == 0; },
        delegate (LadderLottery mod) { return mod.Bomb.GetOnIndicators().Count() > mod.Bomb.GetOffIndicators().Count(); },
        delegate (LadderLottery mod) { return mod.Bomb.IsIndicatorOn(Indicator.FRK) || mod.Bomb.IsIndicatorOn(Indicator.IND) || mod.Bomb.IsIndicatorOn(Indicator.MSA); },
        delegate (LadderLottery mod) { return mod.Bomb.GetBatteryCount() >= 3; },
        delegate (LadderLottery mod) { return mod.Bomb.GetSolvedModuleNames().Count() > mod.Bomb.GetSolvableModuleNames().Count() / 2; },
        delegate (LadderLottery mod) { return mod.Bomb.IsIndicatorOff(Indicator.FRQ) || mod.Bomb.IsIndicatorOff(Indicator.SND) || mod.Bomb.IsIndicatorOff(Indicator.NSA); },
        delegate (LadderLottery mod) { return mod.Bomb.GetTime() < mod._initialTime / 2; }
    };
    private List<Link.Column> _columns = new List<Link.Column>{
        Link.Column.Two,
        Link.Column.Three,
        Link.Column.One,
        Link.Column.Three,
        Link.Column.Three,
        Link.Column.One,
        Link.Column.One,
        Link.Column.Two,
        Link.Column.One,
        Link.Column.Two,
        Link.Column.Three,
        Link.Column.Two
    };
    private Link.Point _startingPoint;

    // Use this for initialization
    void Start()
    {
        Debug.LogFormat("OK");
        _moduleId = _moduleIdCounter++;
        _initialTime = Bomb.GetTime();

        // rule seed init
        var rnd = RuleSeedable.GetRNG();
        rnd.ShuffleFisherYates(_rules);
        rnd.ShuffleFisherYates(_columns);

        // light scaling
        float scalar = transform.lossyScale.x;
        foreach (MeshRenderer light in TopWires)
            light.GetComponentInChildren<Light>().range *= scalar;

        // initialize links activation
        _links = new List<Link>();
        for (int i = 0; i < 12; ++i)
            _links.Add(new Link(_rules[i], _columns[i]));

        for (int i = 0; i < Buttons.Length; ++i)
        {
            int j = i;
            Buttons[i].OnInteract += delegate { buttonPress(j); return false; };
        }

        // pick the top path
        int startIndex = Rnd.Range(0, 4);
        _startingPoint = (Link.Point)startIndex;
        TopWires[startIndex].GetComponentInChildren<Light>().enabled = true;
        TopWires[startIndex].material = MatWireOn;
        Debug.LogFormat("[Ladder Lottery #{0}] Starting point {1}", _moduleId, _startingPoint.ToString());
    }

    void buttonPress(int index)
    {
        Buttons[index].AddInteractionPunch();
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, transform);
        if (!_isSolved && !_isCoroutineActive)
        {
            Link.Point finalPoint = getRightAnswer();
            if (index == (int)finalPoint)
            {
                Debug.LogFormat("[Ladder Lottery #{0}] Final answer {1} is correct. Module solved.", _moduleId, finalPoint.ToString());
                Module.HandlePass();
                _isSolved = true;
                TopWires[(int)_startingPoint].GetComponentInChildren<Light>().enabled = false;
                TopWires[(int)_startingPoint].material = MatWireOff;
            }
            else
            {
                Debug.LogFormat("[Ladder Lottery #{0}] Strike ! Expected answer {1}. Button {2} pressed.", _moduleId, finalPoint.ToString(), index);
                Module.HandleStrike();
                StartCoroutine(strikeCoroutine(index));
            }
        }
    }

    Link.Point getRightAnswer()
    {
        Link.Point point = _startingPoint;
        foreach (Link link in _links)
        {
            if (link.contains(point) && link.isActive(this))
                point = link.getOther(point);
        }
        return point;
    }

    IEnumerator strikeCoroutine(int index)
    {
        _isCoroutineActive = true;
        for (int i = 0; i < 5; ++i)
        {
            TopWires[(int)_startingPoint].GetComponent<Renderer>().material = MatWireStrike;
            DownWires[index].GetComponent<Renderer>().material = MatWireStrike;
            yield return new WaitForSeconds(.1f);
            TopWires[(int)_startingPoint].GetComponent<Renderer>().material = MatWireOn;
            DownWires[index].GetComponent<Renderer>().material = MatWireOff;
            yield return new WaitForSeconds(.1f);
        }
        _isCoroutineActive = false;
    }

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"!{0} press <#> [Presses wire at position 1-4 or A-D]";
#pragma warning restore 414

    KMSelectable[] ProcessTwitchCommand(string command)
    {
        command = command.ToLowerInvariant();
        var match = Regex.Match(command, @"^\s*(?:p|press)\s*([1-4]|[a-d])\s*");
        Debug.LogFormat("process");
        if (match.Success)
        {
            switch (match.Groups[1].Value)
            {
                case "1":
                case "a":
                    return new[] { Buttons[0] };
                case "2":
                case "b":
                    return new[] { Buttons[1] };
                case "3":
                case "c":
                    return new[] { Buttons[2] };
                case "4":
                case "d":
                    return new[] { Buttons[3] };
            }
        }
        return null;
    }
}
