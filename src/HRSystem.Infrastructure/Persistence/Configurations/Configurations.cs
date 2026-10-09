using HRSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

/// <summary>Fluent configuration for <see cref="Employee"/>.</summary>
public class EmployeeConfig : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.ToTable("Employees");
        b.HasKey(x => x.Id);
        b.Property(x => x.EmployeeNo).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.EmployeeNo).IsUnique();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Position).HasMaxLength(100);
        b.HasOne(x => x.Department).WithMany(d => d.Employees)
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.UserId).HasMaxLength(450);
        b.HasIndex(x => x.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

/// <summary>Fluent configuration for <see cref="Department"/>.</summary>
public class DepartmentConfig : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.ToTable("Departments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Code).HasMaxLength(50);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasOne(x => x.Manager).WithMany()
            .HasForeignKey(x => x.ManagerEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Fluent configuration for <see cref="AccountType"/>.</summary>
public class AccountTypeConfig : IEntityTypeConfiguration<AccountType>
{
    public void Configure(EntityTypeBuilder<AccountType> b)
    {
        b.ToTable("AccountTypes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.GroupName).HasMaxLength(100);
        b.Property(x => x.PrefixText).HasMaxLength(200);
        b.Property(x => x.SuffixText).HasMaxLength(200);
        b.Property(x => x.Column).HasDefaultValue(1);
        b.Property(x => x.HasCheckbox).HasDefaultValue(true);
        b.Property(x => x.DetailLabel).HasMaxLength(100);
        b.HasOne(x => x.ResponsibleDept)
            .WithMany(d => d.AccountTypes)
            .HasForeignKey(x => x.ResponsibleDeptId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Fluent configuration for <see cref="AccountRequest"/>.</summary>
public class AccountRequestConfig : IEntityTypeConfiguration<AccountRequest>
{
    public void Configure(EntityTypeBuilder<AccountRequest> b)
    {
        b.ToTable("AccountRequests");
        b.HasKey(x => x.Id);
        b.Property(x => x.RequestNo).HasMaxLength(30).IsRequired();
        b.HasIndex(x => x.RequestNo).IsUnique();
        b.Property(x => x.Remark).HasMaxLength(1000);
        b.Property(x => x.ReplacementOf).HasMaxLength(100);
        b.Property(x => x.ApproverUserId).HasMaxLength(450);
        b.HasIndex(x => new { x.ApproverUserId, x.Status });
        b.Property(x => x.DecidedBy).HasMaxLength(450);
        b.Property(x => x.DecisionRemark).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Employee)
            .WithMany(e => e.AccountRequests)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Fluent configuration for <see cref="AccountRequestItem"/>.</summary>
public class AccountRequestItemConfig : IEntityTypeConfiguration<AccountRequestItem>
{
    public void Configure(EntityTypeBuilder<AccountRequestItem> b)
    {
        b.ToTable("AccountRequestItems");
        b.HasKey(x => x.Id);
        b.Property(x => x.AccountValue).HasMaxLength(200);
        b.Property(x => x.ResultRemark).HasMaxLength(1000);
        b.Property(x => x.RequestDetail).HasMaxLength(500);
        b.Property(x => x.AssignedUserId).HasMaxLength(450);
        b.HasOne(x => x.Request)
            .WithMany(r => r.Items)
            .HasForeignKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.AccountType)
            .WithMany()
            .HasForeignKey(x => x.AccountTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AssignedDept)
            .WithMany()
            .HasForeignKey(x => x.AssignedDeptId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Fluent configuration for <see cref="Attachment"/>.</summary>
public class AttachmentConfig : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> b)
    {
        b.ToTable("Attachments");
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.FilePath).HasMaxLength(500).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(120);
        b.HasOne(x => x.Request)
            .WithMany(r => r.Attachments)
            .HasForeignKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Fluent configuration for <see cref="EmployeeAttachment"/>.</summary>
public class EmployeeAttachmentConfig : IEntityTypeConfiguration<EmployeeAttachment>
{
    public void Configure(EntityTypeBuilder<EmployeeAttachment> b)
    {
        b.ToTable("EmployeeAttachments");
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.FilePath).HasMaxLength(500).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(120);
        b.HasOne(x => x.Employee)
            .WithMany(e => e.Attachments)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Fluent configuration for <see cref="EmployeeAccount"/>.</summary>
public class EmployeeAccountConfig : IEntityTypeConfiguration<EmployeeAccount>
{
    public void Configure(EntityTypeBuilder<EmployeeAccount> b)
    {
        b.ToTable("EmployeeAccounts");
        b.HasKey(x => x.Id);
        b.Property(x => x.AccountValue).HasMaxLength(200);
        b.HasIndex(x => new { x.EmployeeId, x.AccountTypeId });
        b.HasOne(x => x.Employee)
            .WithMany(e => e.Accounts)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.AccountType)
            .WithMany()
            .HasForeignKey(x => x.AccountTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ChecklistRequestConfig : IEntityTypeConfiguration<ChecklistRequest>
{
    public void Configure(EntityTypeBuilder<ChecklistRequest> b)
    {
        b.ToTable("ChecklistRequests");
        b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.RequestNo).HasMaxLength(30).IsRequired();
        b.HasIndex(x => x.RequestNo).IsUnique();
        b.HasIndex(x => new { x.Kind, x.EmployeeId, x.Status });
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.Remark).HasMaxLength(1000);
        b.Property(x => x.SubmittedBy).HasMaxLength(450);
        b.Property(x => x.FinalizedBy).HasMaxLength(450);
        b.HasOne(x => x.Employee)
            .WithMany(e => e.ChecklistRequests)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AccountRequest)
            .WithMany()
            .HasForeignKey(x => x.AccountRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ChecklistTaskConfig : IEntityTypeConfiguration<ChecklistTask>
{
    public void Configure(EntityTypeBuilder<ChecklistTask> b)
    {
        b.ToTable("ChecklistTasks");
        b.HasKey(x => x.Id);
        b.Property(x => x.DepartmentName).HasMaxLength(100).IsRequired();
        b.Property(x => x.AssignedUserId).HasMaxLength(450);
        b.Property(x => x.AssignedUserName).HasMaxLength(256);
        b.Property(x => x.TaskRemark).HasMaxLength(1000);
        b.Property(x => x.HandledBy).HasMaxLength(450);
        b.HasIndex(x => new { x.ChecklistRequestId, x.SortOrder });
        b.HasIndex(x => new { x.AssignedUserId, x.Status });
        b.HasOne(x => x.ChecklistRequest)
            .WithMany(r => r.Tasks)
            .HasForeignKey(x => x.ChecklistRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ChecklistTaskItemConfig : IEntityTypeConfiguration<ChecklistTaskItem>
{
    public void Configure(EntityTypeBuilder<ChecklistTaskItem> b)
    {
        b.ToTable("ChecklistTaskItems");
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.Property(x => x.Remark).HasMaxLength(1000);
        b.Property(x => x.CompletedBy).HasMaxLength(450);
        b.HasIndex(x => new { x.ChecklistTaskId, x.SortOrder });
        b.HasOne(x => x.ChecklistTask)
            .WithMany(t => t.Items)
            .HasForeignKey(x => x.ChecklistTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ChecklistTemplateConfig : IEntityTypeConfiguration<ChecklistTemplate>
{
    public void Configure(EntityTypeBuilder<ChecklistTemplate> b)
    {
        b.ToTable("ChecklistTemplates");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.Kind, x.DepartmentId }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasOne(x => x.Department)
            .WithMany()
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ChecklistTemplateItemConfig : IEntityTypeConfiguration<ChecklistTemplateItem>
{
    public void Configure(EntityTypeBuilder<ChecklistTemplateItem> b)
    {
        b.ToTable("ChecklistTemplateItems");
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.HasIndex(x => new { x.ChecklistTemplateId, x.SortOrder });
        b.HasOne(x => x.ChecklistTemplate)
            .WithMany(t => t.Items)
            .HasForeignKey(x => x.ChecklistTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Fluent configuration for <see cref="AuditLog"/>.</summary>
public class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityName).HasMaxLength(100);
        b.Property(x => x.EntityId).HasMaxLength(50);
        b.Property(x => x.UserId).HasMaxLength(450);
        b.Property(x => x.UserName).HasMaxLength(256);
        b.HasIndex(x => x.Timestamp);
    }
}
