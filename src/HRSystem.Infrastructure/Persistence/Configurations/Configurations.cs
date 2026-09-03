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
        b.Property(x => x.Department).HasMaxLength(100);
        b.Property(x => x.Position).HasMaxLength(100);
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
        b.Property(x => x.HeadUserId).HasMaxLength(450);
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
