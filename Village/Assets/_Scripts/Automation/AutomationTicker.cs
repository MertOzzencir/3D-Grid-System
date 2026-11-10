using System.Collections.Generic;
using UnityEngine;

// Otomasyonun tek saati: bütün makineler ve kollar aynı sabit adımla (Step) ve hep aynı sırayla işler (redstone gibi
// öngörülebilir). Her adımda önce makineler (üretir), sonra kollar (taşır). Sahnede kurulum yok: ilk kayıtta kendi
// objesini yaratır.
public interface IAutomationTick
{
    void AutomationTick(float step);
}

public static class AutomationTicker
{
    public const float Step = 0.1f; // saniye (saniyede 10 adım)

    private static readonly List<IAutomationTick> machines = new List<IAutomationTick>();
    private static readonly List<IAutomationTick> arms = new List<IAutomationTick>();
    private static readonly List<IAutomationTick> buffer = new List<IAutomationTick>();
    private static AutomationTickerDriver driver;

    public static void Register(IAutomationTick tick, bool isArm)
    {
        List<IAutomationTick> list = isArm ? arms : machines;
        if (!list.Contains(tick)) list.Add(tick);
        if (driver == null) driver = new GameObject("Automation Ticker").AddComponent<AutomationTickerDriver>();
    }

    public static void Unregister(IAutomationTick tick)
    {
        machines.Remove(tick);
        arms.Remove(tick);
    }

    internal static void RunStep()
    {
        Run(machines);
        Run(arms);
    }

    private static void Run(List<IAutomationTick> list)
    {
        buffer.Clear();
        buffer.AddRange(list); // adım içinde kayıt / silme olabilir
        foreach (IAutomationTick tick in buffer)
            if (tick is Object o && o != null) tick.AutomationTick(Step);
    }
}

public class AutomationTickerDriver : MonoBehaviour
{
    private float accumulator;

    private void Update()
    {
        accumulator += Time.deltaTime;
        int guard = 0;
        while (accumulator >= AutomationTicker.Step && guard++ < 10) // takılan karede adımlar birikip patlamasın
        {
            accumulator -= AutomationTicker.Step;
            AutomationTicker.RunStep();
        }
        if (guard >= 10) accumulator = 0f;
    }
}
