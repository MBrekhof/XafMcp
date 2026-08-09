using DevExpress.ExpressApp.Design;
using DevExpress.ExpressApp.EFCore.DesignTime;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Microsoft.EntityFrameworkCore;

namespace XafMcp.Module.BusinessObjects;

[TypesInfoInitializer(typeof(DbContextTypesInfoInitializer<XafMcpEFCoreDbContext>))]
public class XafMcpEFCoreDbContext : DbContext {
    public XafMcpEFCoreDbContext(DbContextOptions<XafMcpEFCoreDbContext> options) : base(options) { }
    public DbSet<ModelDifference> ModelDifferences { get; set; }
    public DbSet<ModelDifferenceAspect> ModelDifferenceAspects { get; set; }
    public DbSet<PermissionPolicyRole> Roles { get; set; }
    public DbSet<PermissionPolicyUser> Users { get; set; }
    public DbSet<Region> Regions { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Person> Persons { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderLine> OrderLines { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<ProjectTask> ProjectTasks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseDeferredDeletion(this);
        modelBuilder.UseOptimisticLock();
        modelBuilder.SetOneToManyAssociationDeleteBehavior(DeleteBehavior.SetNull, DeleteBehavior.Cascade);
        modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues);
        modelBuilder.UsePropertyAccessMode(PropertyAccessMode.PreferFieldDuringConstruction);
        modelBuilder.Entity<ModelDifference>()
            .HasMany(t => t.Aspects)
            .WithOne(t => t.Owner)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Person>().Property(p => p.HourlyRate).HasPrecision(19, 4);
        modelBuilder.Entity<Product>().Property(p => p.UnitPrice).HasPrecision(19, 4);
        modelBuilder.Entity<Order>().Property(p => p.Total).HasPrecision(19, 4);
        modelBuilder.Entity<OrderLine>().Property(p => p.UnitPrice).HasPrecision(19, 4);
        modelBuilder.Entity<OrderLine>().Property(p => p.Discount).HasPrecision(19, 4);
        modelBuilder.Entity<Project>().Property(p => p.Budget).HasPrecision(19, 4);
        modelBuilder.Entity<ProjectTask>().Property(p => p.EstimatedHours).HasPrecision(19, 4);
        modelBuilder.Entity<ProjectTask>().Property(p => p.ActualHours).HasPrecision(19, 4);
    }
}
