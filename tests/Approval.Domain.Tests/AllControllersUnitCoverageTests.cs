using System.Security.Claims;
using Approval.Api.Controllers;
using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Approval.Application.Services;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using Approval.Infrastructure.Services;
using Approval.SapAdapter;
using Approval.SapAdapter.Adapters;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TaskStatus = Approval.Domain.Enums.TaskStatus;

namespace Approval.Domain.Tests;

public class AllControllersUnitCoverageTests : IDisposable
{
    private readonly ApprovalDbContext _db;
    private readonly ITraceContext _traceContext = new TestTraceContext();
    private readonly UserDirectoryService _userDirectory;
    private readonly WorkflowRuleMatcher _ruleMatcher;
    private readonly WorkflowEngine _engine;
    private readonly SapMetadataService _metadataService;
    private readonly SapAdapterRegistry _adapterRegistry;

    public AllControllersUnitCoverageTests()
    {
        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseInMemoryDatabase($"ApprovalDb_ControllersTest_{Guid.NewGuid():N}")
            .Options;
        _db = new ApprovalDbContext(options);
        _userDirectory = new UserDirectoryService(_db);
        _ruleMatcher = new WorkflowRuleMatcher(_db);
        _engine = new WorkflowEngine(_db, _traceContext, _userDirectory, _ruleMatcher);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ApprovalDbConnection"] = ""
        }).Build();
        _metadataService = new SapMetadataService(config, NullLogger<SapMetadataService>.Instance);
        _adapterRegistry = new SapAdapterRegistry(new ISapObjectAdapter[] { new FakeObjectAdapter("CHORDR") });
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ApprovalObjectsController_SubmitApproval_ShouldStartWorkflow()
    {
        var graphJson = """{"allowSubmitterRevoke":true,"nodes":[{"nodeKey":"start","nodeType":"Start"},{"nodeKey":"n1","nodeType":"Approval","candidateType":"Direct","candidateValues":["manager"]},{"nodeKey":"end","nodeType":"End"}],"edges":[{"fromNodeKey":"start","toNodeKey":"n1"},{"fromNodeKey":"n1","toNodeKey":"end"}]}""";
        var def = new WorkflowDefinition { Id = "DEF-SUBMIT", Name = "提交审批测试" };
        var ver = new WorkflowDefinitionVersion { Id = "VER-SUBMIT", DefinitionId = "DEF-SUBMIT", GraphJson = graphJson, Status = "Published" };
        var binding = new WorkflowBinding { CompanyId = "DB_KCC", ObjectCode = "CHORDR", VersionId = "VER-SUBMIT", Priority = 1, IsActive = true };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Bindings.Add(binding);
        await _db.SaveChangesAsync();

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "manager"),
            new Claim(ClaimTypes.Name, "管理员")
        }, "TestAuth"));

        var httpContext = new DefaultHttpContext { User = user };
        var controller = new ApprovalObjectsController(_engine, _adapterRegistry, _db, _traceContext)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        // 1. 第一次提交 (带 Idempotency-Key) -> 应当返回 Ok
        var resSubmit1 = await controller.SubmitApproval("CHORDR", "1001", "DB_KCC", "idem_submit_001", CancellationToken.None);
        resSubmit1.Result.Should().BeOfType<OkObjectResult>();

        // 2. 第二次重复提交相同 Idempotency-Key -> 应当幂等命中返回 Ok
        var resSubmit2 = await controller.SubmitApproval("CHORDR", "1001", "DB_KCC", "idem_submit_001", CancellationToken.None);
        resSubmit2.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task MetadataController_Endpoints_ShouldReturnOk()
    {
        var controller = new MetadataController(_metadataService, _traceContext);

        var resComp = await controller.GetCompanyInfo("DB_KCC");
        resComp.Result.Should().BeOfType<OkObjectResult>();

        var res1 = await controller.GetObjectMetadata("CHORDR", "DB_KCC", false);
        res1.Result.Should().BeOfType<OkObjectResult>();

        var res2 = await controller.RefreshAllMetadata("DB_KCC");
        res2.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task DefinitionsController_Endpoints_ShouldCreateAndPublish()
    {
        var controller = new DefinitionsController(_db, _traceContext)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var createDto = new CreateDefinitionDto("DEF_TEST", "测试流程", "General", "描述", null);
        var resCreate = await controller.CreateDefinition(createDto);
        resCreate.Result.Should().BeOfType<OkObjectResult>();

        var resList = controller.GetDefinitions();
        resList.Result.Should().BeOfType<OkObjectResult>();

        var resDetail = controller.GetDefinitionDetail("DEF_TEST");
        resDetail.Result.Should().BeOfType<OkObjectResult>();

        var graphJson = """{"allowSubmitterRevoke":true,"nodes":[{"nodeKey":"start","nodeType":"Start"},{"nodeKey":"end","nodeType":"End"}],"edges":[{"fromNodeKey":"start","toNodeKey":"end"}]}""";
        var pubDto = new PublishVersionDto(graphJson);
        var resPub = await controller.PublishNewVersion("DEF_TEST", pubDto);
        resPub.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task InstancesController_Endpoints_ShouldRevokeAndGetAudit()
    {
        var inst = new WorkflowInstance
        {
            Id = "inst_audit_test",
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "1001",
            SubmitterCode = "manager",
            Status = WorkflowStatus.Running
        };
        _db.Instances.Add(inst);
        await _db.SaveChangesAsync();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Approval-User"] = "manager";

        var controller = new InstancesController(_db, _engine, _traceContext)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var resAudit = controller.GetInstanceAudit("inst_audit_test");
        resAudit.Result.Should().BeOfType<OkObjectResult>();

        var revokeDto = new RevokeRequestDto("发起人撤回测试");
        var resRevoke = await controller.RevokeInstance("inst_audit_test", revokeDto);
        resRevoke.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task TasksController_Endpoints_ShouldListAndMakeDecisionAndForward()
    {
        var graphJson = """{"allowSubmitterRevoke":true,"nodes":[{"nodeKey":"start","nodeType":"Start"},{"nodeKey":"n1","nodeType":"Approval","candidateType":"Direct","candidateValues":["manager"]},{"nodeKey":"end","nodeType":"End"}],"edges":[{"fromNodeKey":"start","toNodeKey":"n1"},{"fromNodeKey":"n1","toNodeKey":"end"}]}""";
        var def = new WorkflowDefinition { Id = "DEF-TASK-TEST", Name = "任务流转测试" };
        var ver = new WorkflowDefinitionVersion { Id = "VER-TASK-TEST", DefinitionId = "DEF-TASK-TEST", GraphJson = graphJson, Status = "Published" };
        var binding = new WorkflowBinding { CompanyId = "DB_KCC", ObjectCode = "CHORDR", VersionId = "VER-TASK-TEST", Priority = 1, IsActive = true };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Bindings.Add(binding);
        await _db.SaveChangesAsync();

        var payload = new SapObjectPayload { CompanyId = "DB_KCC", ObjectCode = "CHORDR", ObjectKey = "1002", DocTotal = 500m, Title = "任务单", CreatorUserCode = "user01", RawJson = "{}" };
        var inst = await _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1002", "user01", "用户", payload);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "manager"),
            new Claim(ClaimTypes.Name, "管理员")
        }, "TestAuth"));

        var httpContext = new DefaultHttpContext { User = user };

        var controller = new TasksController(_engine, _db, _traceContext, new Microsoft.Extensions.Logging.Abstractions.NullLogger<TasksController>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var resGet = controller.GetTasks("mine", "pending");
        resGet.Result.Should().BeOfType<OkObjectResult>();

        var task = await _db.Tasks.FirstAsync(t => t.InstanceId == inst.Id);

        var decisionReq = new DecisionRequest("Approve", "核准通过");
        var resDecision = await controller.MakeDecision(task.Id, decisionReq, "idem_decision_001", CancellationToken.None);
        resDecision.Result.Should().BeOfType<OkObjectResult>();

        // 幂等请求相同 decision
        var resDecisionCached = await controller.MakeDecision(task.Id, decisionReq, "idem_decision_001", CancellationToken.None);
        resDecisionCached.Result.Should().BeOfType<OkObjectResult>();

        // 插入第二条任务测试 Forward
        var payload2 = new SapObjectPayload { CompanyId = "DB_KCC", ObjectCode = "CHORDR", ObjectKey = "1003", DocTotal = 500m, Title = "转办单", CreatorUserCode = "user01", RawJson = "{}" };
        var inst2 = await _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1003", "user01", "用户", payload2);
        var task2 = await _db.Tasks.FirstAsync(t => t.InstanceId == inst2.Id);

        var fwdReq = new ForwardRequest("user_target", "目标人", "请帮忙审核");
        var resFwd = await controller.ForwardTask(task2.Id, fwdReq, null, CancellationToken.None);
        resFwd.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void SnapshotCompressionHelper_ShouldCompressAndDecompress()
    {
        var rawJson = "{\"DocTotal\": 99999.0, \"Lines\": [" + string.Join(",", Enumerable.Range(1, 20).Select(i => $"{{\"LineNum\": {i}, \"ItemCode\": \"ITEM_{i:D4}\", \"Price\": 100.0}}")) + "]}";

        var compressed = Approval.Application.Common.Helpers.SnapshotCompressionHelper.CompressJson(rawJson);
        compressed.Should().StartWith("BR64:");

        var decompressed = Approval.Application.Common.Helpers.SnapshotCompressionHelper.DecompressJson(compressed);
        decompressed.Should().Be(rawJson);
    }
}
