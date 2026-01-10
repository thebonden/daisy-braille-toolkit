using DAISY_Braille_Toolkit.Models;

namespace DAISY_Braille_Toolkit.Services;

public sealed class PipelineRunner
{
    private readonly JobStore _store = new();

    public async Task RunAsync(
        JobManifest job,
        PipelineStep? forceStartAt,
        Action<string> log,
        Action<double, string> progress)
    {
        var steps = GetPlan(job.Mode);

        // Find start-step: Auto = første NotStarted/Failed
        var startIndex = 0;
        if (forceStartAt is null)
        {
            startIndex = steps.FindIndex(s =>
                job.Steps[s].Status is StepStatus.NotStarted or StepStatus.Failed);

            if (startIndex < 0)
            {
                progress(1, "All done");
                return;
            }
        }
        else
        {
            startIndex = steps.IndexOf(forceStartAt.Value);
            if (startIndex < 0) startIndex = 0;

            // Hvis man tvinger start fra et step, så nulstil efterfølgende steps
            for (var i = startIndex; i < steps.Count; i++)
                job.Steps[steps[i]] = new StepState();

            _store.Save(job.OutputRoot, job);
        }

        for (var i = startIndex; i < steps.Count; i++)
        {
            var step = steps[i];
            var pct = (double)i / steps.Count;

            await RunStep(step, job, log, msg => progress(pct, msg));
            _store.Save(job.OutputRoot, job);
        }

        progress(1, "Færdig");
    }

    private static List<PipelineStep> GetPlan(OutputMode mode)
    {
        var plan = new List<PipelineStep> { PipelineStep.Import, PipelineStep.DtBook, PipelineStep.Tts };

        if (mode is OutputMode.DaisyOnly or OutputMode.Both)
            plan.Add(PipelineStep.DaisyBuild);

        if (mode is OutputMode.BrailleOnly or OutputMode.Both)
            plan.Add(PipelineStep.PefBuild);

        plan.Add(PipelineStep.IsoAndCsv);
        return plan;
    }

    private async Task RunStep(PipelineStep step, JobManifest job, Action<string> log, Action<string> status)
    {
        var state = job.Steps[step];
        state.Status = StepStatus.Running;
        state.StartedUtc = DateTime.UtcNow;
        state.Error = null;

        try
        {
            status($"Kører: {step}");
            log($"== {step} ==");

            // TODO: Koble rigtige steps på (Pipeline2 + ElevenLabs + ISO + CSV).
            // Dette er en "sikker" skeleton, så resume/checkpoints virker fra dag 1.
            await Task.Delay(400);

            state.Status = StepStatus.Completed;
            state.FinishedUtc = DateTime.UtcNow;
            log($"OK: {step}");
        }
        catch (Exception ex)
        {
            state.Status = StepStatus.Failed;
            state.FinishedUtc = DateTime.UtcNow;
            state.Error = ex.ToString();
            log($"FEJL: {step}\n{ex}");
            throw;
        }
    }
}
