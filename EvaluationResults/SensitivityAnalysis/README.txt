PCG-VFX one-at-a-time sensitivity analysis

Design:
- Static seed = 99; dynamic seed base = 99.
- Baseline uses the final, unmodified configuration.
- Each non-baseline scenario changes one parameter by -10% or +10%.
- Weight groups are renormalised to sum to one after a component is perturbed.
- The dynamic trajectory uses its authored smoothing override of 0.75 as its baseline.

Files:
- SensitivitySummary.csv: one row per scenario for graphing and thesis tables.
- SensitivityStaticDetails.csv: one row per static profile and scenario.
- SensitivityDynamicDetails.csv: one row per dynamic node and scenario.
- SensitivityDecayAudit.csv: all decay checks for all scenarios.

Interpretation:
- Delta columns compare a scenario against Baseline.
- Theme/slot changes count outputs that changed relative to Baseline.
- The analysis identifies sensitive parameters; it does not prove a universally optimal parameter value.
