using Approval.Application.Common.Interfaces;
using Approval.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Approval.Infrastructure.Persistence;

/// <summary>
/// ApprovalDB EF Core 数据库上下文
/// </summary>
public class ApprovalDbContext : DbContext, IApprovalDbContext
{
    public ApprovalDbContext(DbContextOptions<ApprovalDbContext> options) : base(options)
    {
    }

    public DbSet<WorkflowDefinition> Definitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowDefinitionVersion> DefinitionVersions => Set<WorkflowDefinitionVersion>();
    public DbSet<WorkflowBinding> Bindings => Set<WorkflowBinding>();
    public DbSet<WorkflowRule> Rules => Set<WorkflowRule>();
    public DbSet<WorkflowInstance> Instances => Set<WorkflowInstance>();
    public DbSet<WorkflowSnapshot> Snapshots => Set<WorkflowSnapshot>();
    public DbSet<WorkflowNodeInstance> NodeInstances => Set<WorkflowNodeInstance>();
    public DbSet<WorkflowTask> Tasks => Set<WorkflowTask>();
    public DbSet<WorkflowTaskCandidate> TaskCandidates => Set<WorkflowTaskCandidate>();
    public DbSet<WorkflowActionLog> ActionLogs => Set<WorkflowActionLog>();
    public DbSet<WorkflowOutbox> Outboxes => Set<WorkflowOutbox>();
    public DbSet<WorkflowInbox> Inboxes => Set<WorkflowInbox>();
    public DbSet<SapSyncState> SapSyncStates => Set<SapSyncState>();
    public DbSet<SysUserMapping> UserMappings => Set<SysUserMapping>();
    public DbSet<SysNotification> Notifications => Set<SysNotification>();
    public DbSet<SysUiLayout> UiLayouts => Set<SysUiLayout>();

    IQueryable<WorkflowDefinition> IApprovalDbContext.Definitions => Definitions;
    IQueryable<WorkflowDefinitionVersion> IApprovalDbContext.DefinitionVersions => DefinitionVersions;
    IQueryable<WorkflowBinding> IApprovalDbContext.Bindings => Bindings;
    IQueryable<WorkflowRule> IApprovalDbContext.Rules => Rules;
    IQueryable<WorkflowInstance> IApprovalDbContext.Instances => Instances;
    IQueryable<WorkflowSnapshot> IApprovalDbContext.Snapshots => Snapshots;
    IQueryable<WorkflowNodeInstance> IApprovalDbContext.NodeInstances => NodeInstances;
    IQueryable<WorkflowTask> IApprovalDbContext.Tasks => Tasks;
    IQueryable<WorkflowTaskCandidate> IApprovalDbContext.TaskCandidates => TaskCandidates;
    IQueryable<WorkflowActionLog> IApprovalDbContext.ActionLogs => ActionLogs;
    IQueryable<WorkflowOutbox> IApprovalDbContext.Outboxes => Outboxes;
    IQueryable<WorkflowInbox> IApprovalDbContext.Inboxes => Inboxes;
    IQueryable<SapSyncState> IApprovalDbContext.SapSyncStates => SapSyncStates;
    IQueryable<SysUserMapping> IApprovalDbContext.UserMappings => UserMappings;
    IQueryable<SysNotification> IApprovalDbContext.Notifications => Notifications;
    IQueryable<SysUiLayout> IApprovalDbContext.UiLayouts => UiLayouts;

    public new async Task AddAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : class
    {
        await Set<TEntity>().AddAsync(entity, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // wf_definition
        modelBuilder.Entity<WorkflowDefinition>(b =>
        {
            b.ToTable("wf_definition");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(64);
            b.Property(x => x.Name).HasMaxLength(128).IsRequired();
            b.Property(x => x.Category).HasMaxLength(64).HasDefaultValue("General");
            b.Property(x => x.Description).HasMaxLength(500);
        });

        // wf_definition_version
        modelBuilder.Entity<WorkflowDefinitionVersion>(b =>
        {
            b.ToTable("wf_definition_version");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(64);
            b.Property(x => x.DefinitionId).HasMaxLength(64).IsRequired();
            b.Property(x => x.Status).HasMaxLength(32).HasDefaultValue("Draft");
            b.Property(x => x.CreatedBy).HasMaxLength(64).IsRequired();
            b.HasIndex(x => new { x.DefinitionId, x.VersionNum }).IsUnique();
            b.HasOne(x => x.Definition).WithMany(x => x.Versions).HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        });

        // wf_binding
        modelBuilder.Entity<WorkflowBinding>(b =>
        {
            b.ToTable("wf_binding");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(64);
            b.Property(x => x.CompanyId).HasMaxLength(64).IsRequired();
            b.Property(x => x.ObjectCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.VersionId).HasMaxLength(64).IsRequired();
            b.Property(x => x.ConditionExpr).HasMaxLength(500);
            b.HasIndex(x => new { x.CompanyId, x.ObjectCode, x.IsActive, x.Priority });
            b.HasOne(x => x.Version).WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Restrict);
        });

        // wf_rule
        modelBuilder.Entity<WorkflowRule>(b =>
        {
            b.ToTable("wf_rule");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(64);
            b.Property(x => x.CompanyId).HasMaxLength(64).IsRequired();
            b.Property(x => x.ObjectCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.ObjectType).HasMaxLength(32).HasDefaultValue("Document");
            b.Property(x => x.RuleName).HasMaxLength(128).IsRequired();
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.TriggerMode).HasMaxLength(32).HasDefaultValue("AutoAlways");
            b.Property(x => x.TriggerFieldName).HasMaxLength(64);
            b.Property(x => x.UserScopeMode).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.ConditionExpr).HasMaxLength(500);
            b.Property(x => x.TargetDefinitionId).HasMaxLength(64).IsRequired();
            b.Property(x => x.TargetVersionId).HasMaxLength(64);
            b.HasIndex(x => new { x.CompanyId, x.ObjectCode, x.IsActive, x.Priority });
            b.HasOne(x => x.TargetDefinition).WithMany().HasForeignKey(x => x.TargetDefinitionId).OnDelete(DeleteBehavior.Restrict);
        });

        // wf_instance
        modelBuilder.Entity<WorkflowInstance>(b =>
        {
            b.ToTable("wf_instance");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(64);
            b.Property(x => x.CompanyId).HasMaxLength(64).IsRequired();
            b.Property(x => x.ObjectCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.ObjectKey).HasMaxLength(128).IsRequired();
            b.Property(x => x.Title).HasMaxLength(256).IsRequired();
            b.Property(x => x.SubmitterCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.SubmitterName).HasMaxLength(128);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.CurrentVersionId).HasMaxLength(64).IsRequired();
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => new { x.CompanyId, x.ObjectCode, x.ObjectKey, x.Status });
            b.HasIndex(x => new { x.CompanyId, x.ObjectCode, x.ObjectKey })
                .HasDatabaseName("UX_wf_instance_running_object")
                .HasFilter("[status] = 'Running'")
                .IsUnique();
            b.HasOne(x => x.CurrentVersion).WithMany().HasForeignKey(x => x.CurrentVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        // wf_snapshot
        modelBuilder.Entity<WorkflowSnapshot>(b =>
        {
            b.ToTable("wf_snapshot");
            b.HasKey(x => x.InstanceId);
            b.Property(x => x.InstanceId).HasMaxLength(64);
            b.Property(x => x.DataSha256).HasMaxLength(64).IsRequired();
            b.HasOne(x => x.Instance).WithOne(x => x.Snapshot).HasForeignKey<WorkflowSnapshot>(x => x.InstanceId).OnDelete(DeleteBehavior.Cascade);
        });

        // wf_node_instance
        modelBuilder.Entity<WorkflowNodeInstance>(b =>
        {
            b.ToTable("wf_node_instance");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(64);
            b.Property(x => x.InstanceId).HasMaxLength(64).IsRequired();
            b.Property(x => x.NodeKey).HasMaxLength(64).IsRequired();
            b.Property(x => x.NodeName).HasMaxLength(128).IsRequired();
            b.Property(x => x.NodeType).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.HasOne(x => x.Instance).WithMany(x => x.NodeInstances).HasForeignKey(x => x.InstanceId).OnDelete(DeleteBehavior.Cascade);
        });

        // wf_task
        modelBuilder.Entity<WorkflowTask>(b =>
        {
            b.ToTable("wf_task");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(64);
            b.Property(x => x.InstanceId).HasMaxLength(64).IsRequired();
            b.Property(x => x.NodeInstanceId).HasMaxLength(64).IsRequired();
            b.Property(x => x.TaskType).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.CompletedBy).HasMaxLength(64);
            b.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.Comments).HasMaxLength(1000);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => new { x.Status, x.DueAt });
            b.HasIndex(x => new { x.Status, x.CreatedAt });
            b.HasIndex(x => new { x.Status, x.InstanceId, x.CreatedAt })
                .HasDatabaseName("IX_wf_task_pending_filtered")
                .HasFilter("[status] = 'Pending'");
            b.HasOne(x => x.Instance).WithMany(x => x.Tasks).HasForeignKey(x => x.InstanceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.NodeInstance).WithMany(x => x.Tasks).HasForeignKey(x => x.NodeInstanceId).OnDelete(DeleteBehavior.Restrict);
        });

        // wf_task_candidate
        modelBuilder.Entity<WorkflowTaskCandidate>(b =>
        {
            b.ToTable("wf_task_candidate");
            b.HasKey(x => x.Id);
            b.Property(x => x.TaskId).HasMaxLength(64).IsRequired();
            b.Property(x => x.UserCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.UserName).HasMaxLength(128);
            b.Property(x => x.CandidateType).HasConversion<string>().HasMaxLength(32);
            b.HasIndex(x => new { x.UserCode, x.TaskId });
            b.HasIndex(x => new { x.TaskId, x.UserCode }).IsUnique();
            b.HasOne(x => x.Task).WithMany(x => x.Candidates).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        });

        // wf_action_log
        modelBuilder.Entity<WorkflowActionLog>(b =>
        {
            b.ToTable("wf_action_log");
            b.HasKey(x => x.Id);
            b.Property(x => x.TraceId).HasMaxLength(64).IsRequired();
            b.Property(x => x.InstanceId).HasMaxLength(64).IsRequired();
            b.Property(x => x.TaskId).HasMaxLength(64);
            b.Property(x => x.OperatorCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.OperatorName).HasMaxLength(128);
            b.Property(x => x.Action).HasMaxLength(64).IsRequired();
            b.Property(x => x.FromStatus).HasMaxLength(32).IsRequired();
            b.Property(x => x.ToStatus).HasMaxLength(32).IsRequired();
            b.Property(x => x.Comment).HasMaxLength(1000);
            b.Property(x => x.ClientIp).HasMaxLength(64);
            b.HasIndex(x => new { x.InstanceId, x.ActionTime });
            b.HasOne(x => x.Instance).WithMany(x => x.ActionLogs).HasForeignKey(x => x.InstanceId).OnDelete(DeleteBehavior.Restrict);
        });

        // wf_outbox
        modelBuilder.Entity<WorkflowOutbox>(b =>
        {
            b.ToTable("wf_outbox");
            b.HasKey(x => x.Id);
            b.Property(x => x.TraceId).HasMaxLength(64).IsRequired();
            b.Property(x => x.EventType).HasMaxLength(64).IsRequired();
            b.Property(x => x.AggregateId).HasMaxLength(64).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.LockId).HasMaxLength(64);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => new { x.Status, x.NextRetryAt });
            b.HasIndex(x => new { x.Status, x.NextRetryAt, x.CreatedAt })
                .HasDatabaseName("IX_wf_outbox_unprocessed_filtered")
                .HasFilter("[status] = 'Pending' OR [status] = 'Processing'");
        });

        // wf_inbox
        modelBuilder.Entity<WorkflowInbox>(b =>
        {
            b.ToTable("wf_inbox");
            b.HasKey(x => x.Id);
            b.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
            b.Property(x => x.HandlerName).HasMaxLength(128).IsRequired();
            b.HasIndex(x => new { x.HandlerName, x.IdempotencyKey }).IsUnique();
        });

        // sap_sync_state
        modelBuilder.Entity<SapSyncState>(b =>
        {
            b.ToTable("sap_sync_state");
            b.HasKey(x => x.Id);
            b.Property(x => x.CompanyId).HasMaxLength(64).IsRequired();
            b.Property(x => x.ObjectCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.ObjectKey).HasMaxLength(128).IsRequired();
            b.Property(x => x.InstanceId).HasMaxLength(64).IsRequired();
            b.Property(x => x.ExpectedStatus).HasMaxLength(32).IsRequired();
            b.Property(x => x.LastSyncedStatus).HasMaxLength(32);
            b.Property(x => x.SyncStatus).HasMaxLength(32);
            b.HasIndex(x => new { x.CompanyId, x.ObjectCode, x.ObjectKey }).IsUnique();
        });

        // sys_user_mapping
        modelBuilder.Entity<SysUserMapping>(b =>
        {
            b.ToTable("sys_user_mapping");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(64);
            b.Property(x => x.SapUserCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.AdUserCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
            b.Property(x => x.Department).HasMaxLength(128);
            b.Property(x => x.ManagerCode).HasMaxLength(64);
            b.Property(x => x.Roles).HasMaxLength(500);
            b.Property(x => x.DelegateUserCode).HasMaxLength(64);
            b.HasIndex(x => x.SapUserCode).IsUnique();
            b.HasIndex(x => x.AdUserCode).IsUnique();
        });

        // sys_notification
        modelBuilder.Entity<SysNotification>(b =>
        {
            b.ToTable("sys_notification");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(64);
            b.Property(x => x.RecipientUserCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.SenderUserCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.InstanceId).HasMaxLength(64);
            b.Property(x => x.ObjectCode).HasMaxLength(64);
            b.Property(x => x.ObjectKey).HasMaxLength(128);
            b.Property(x => x.Title).HasMaxLength(256).IsRequired();
            b.Property(x => x.Type).HasMaxLength(32).IsRequired();
            b.HasIndex(x => new { x.RecipientUserCode, x.IsRead, x.CreatedAt });
        });

        // sys_ui_layout (企业级全公司/用户分层 UI 布局配置)
        modelBuilder.Entity<SysUiLayout>(b =>
        {
            b.ToTable("sys_ui_layout");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(128);
            b.Property(x => x.CompanyId).HasMaxLength(64).IsRequired();
            b.Property(x => x.ObjectCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.UserCode).HasMaxLength(64);
            b.Property(x => x.ConfigType).HasMaxLength(64).HasDefaultValue("HeaderAndTableLayout");
            b.Property(x => x.UpdatedBy).HasMaxLength(64).IsRequired();
            b.HasIndex(x => new { x.CompanyId, x.ObjectCode, x.UserCode });
        });

        // SQL 初始化脚本使用 snake_case；显式统一列名，避免 EF 默认 PascalCase 与 DDL 不匹配。
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
            property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string value)
    {
        var result = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c) && i > 0) result.Append('_');
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
}
