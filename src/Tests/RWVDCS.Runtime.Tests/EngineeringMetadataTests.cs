using RWVDCS.Engineering;

namespace RWVDCS.Runtime.Tests;

public sealed class EngineeringMetadataTests
{
    [Fact]
    public void Point_identity_priority_and_controller_address_participate_in_fingerprint()
    {
        EngineeringModel baseline = BuildModel();

        Assert.NotEqual(
            ProjectFingerprint.Compute(baseline),
            ProjectFingerprint.Compute(BuildModel(pointId: 102)));
        Assert.NotEqual(
            ProjectFingerprint.Compute(baseline),
            ProjectFingerprint.Compute(BuildModel(lowAlarm1Priority: 2)));
        Assert.NotEqual(
            ProjectFingerprint.Compute(baseline),
            ProjectFingerprint.Compute(BuildModel(controllerAddress: "1002")));
    }

    [Fact]
    public void ModelDiff_reports_point_identity_priorities_and_controller_address()
    {
        EngineeringModel baseline = BuildModel();
        EngineeringModel changed = BuildModel(
            pointId: 102,
            lowAlarm1Priority: 2,
            highAlarm3Priority: 3,
            controllerAddress: "1002");

        ModelDiffReport diff = ModelDiff.Compare(baseline, changed);

        DiffEntry controller = Assert.Single(
            diff.Entries,
            entry => entry.Kind == DiffKind.ControllerChanged);
        Assert.Contains("地址 1001→1002", controller.Detail);

        DiffEntry point = Assert.Single(
            diff.Entries,
            entry => entry.Kind == DiffKind.PointChanged && entry.Name == "AI001");
        Assert.Contains("ID 101→102", point.Detail);
        Assert.Contains("低报警一级优先级 1→2", point.Detail);
        Assert.Contains("高报警三级优先级 1→3", point.Detail);
    }

    private static EngineeringModel BuildModel(
        int pointId = 101,
        int lowAlarm1Priority = 1,
        int highAlarm3Priority = 1,
        string controllerAddress = "1001") => new()
    {
        ProjectPath = "engineering-metadata-test",
        Controllers =
        [
            new ControllerModel
            {
                Id = 1,
                Address = controllerAddress,
                Name = "DPU1",
                Points =
                [
                    new PointModel
                    {
                        ID = pointId,
                        Name = "AI001",
                        DataType = "LA",
                        DefaultValue = 0f,
                        LowAlarm1Priority = lowAlarm1Priority,
                        LowAlarm2Priority = 2,
                        LowAlarm3Priority = 3,
                        HighAlarm1Priority = 3,
                        HighAlarm2Priority = 2,
                        HighAlarm3Priority = highAlarm3Priority,
                    },
                ],
                Blocks = [],
            },
        ],
    };
}
