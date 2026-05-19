using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VIIDII.Models;

namespace VIIDII.Data
{
    public class ViidiiDbContext : DbContext
    {
        public ViidiiDbContext(DbContextOptions<ViidiiDbContext> options) : base(options)
        {
        }

        // DbSets for all entities
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Session> Sessions { get; set; } = null!;
        public DbSet<SessionParticipant> SessionParticipants { get; set; } = null!;
        public DbSet<Message> Messages { get; set; } = null!;
        public DbSet<AttendanceLog> AttendanceLogs { get; set; } = null!;
        public DbSet<FileMetadata> Files { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var departmentsComparer = new ValueComparer<List<User.Departments>>(
                (left, right) =>
                    ReferenceEquals(left, right) ||
                    (left != null && right != null && left.SequenceEqual(right)),
                list => list == null ? 0 : list.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)),
                list => list == null ? new List<User.Departments>() : list.ToList());

            var levelsComparer = new ValueComparer<List<User.Levels>>(
                (left, right) =>
                    ReferenceEquals(left, right) ||
                    (left != null && right != null && left.SequenceEqual(right)),
                list => list == null ? 0 : list.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)),
                list => list == null ? new List<User.Levels>() : list.ToList());

            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.MatricNo).IsUnique();
                entity.Property(e => e.MatricNo).HasMaxLength(20).IsRequired();
                entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
                entity.Property(e => e.PasswordHash).IsRequired();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            });

            // Session configuration
            modelBuilder.Entity<Session>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.SessionId).IsUnique();
                entity.Property(e => e.SessionId).HasMaxLength(20).IsRequired();
                entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
                entity.Property(e => e.LecturerConnectionId).HasMaxLength(500);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

                // Store JSON arrays for Departments and Levels
                entity.Property(e => e.AllowedDepartments)
                    .HasConversion(
                        v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                        v => System.Text.Json.JsonSerializer.Deserialize<List<User.Departments>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<User.Departments>())
                    .Metadata.SetValueComparer(departmentsComparer);

                entity.Property(e => e.AllowedLevels)
                    .HasConversion(
                        v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                        v => System.Text.Json.JsonSerializer.Deserialize<List<User.Levels>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<User.Levels>())
                    .Metadata.SetValueComparer(levelsComparer);

                // Ignore in-memory only properties
                entity.Ignore(e => e.ParticipantIds);
                entity.Ignore(e => e.ParticipantStatuses);
                entity.Ignore(e => e.ParticipantEvents);
                entity.Ignore(e => e.ParticipantConnectionIds);

                // Foreign key to Lecturer (User)
                entity.HasOne(e => e.Lecturer)
                    .WithMany(u => u.LecturerSessions)
                    .HasForeignKey(e => e.LecturerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // SessionParticipant configuration (Junction table)
            modelBuilder.Entity<SessionParticipant>(entity =>
            {
                entity.HasKey(e => new { e.SessionId, e.UserId });
                entity.Property(e => e.JoinedAt).HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(e => e.Session)
                    .WithMany(s => s.Participants)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                    .WithMany(u => u.SessionParticipants)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Message configuration
            modelBuilder.Entity<Message>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Content).IsRequired();
                entity.Property(e => e.Reaction).HasMaxLength(50);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(e => e.Session)
                    .WithMany(s => s.Messages)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Author)
                    .WithMany(u => u.Messages)
                    .HasForeignKey(e => e.AuthorId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Parent)
                    .WithMany(m => m.Replies)
                    .HasForeignKey(e => e.ParentId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // AttendanceLog configuration
            modelBuilder.Entity<AttendanceLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Timestamp).HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(e => e.Session)
                    .WithMany(s => s.AttendanceLogs)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Student)
                    .WithMany(u => u.AttendanceLogs)
                    .HasForeignKey(e => e.StudentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // FileMetadata configuration
            modelBuilder.Entity<FileMetadata>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FileName).HasMaxLength(500).IsRequired();
                entity.Property(e => e.MimeType).HasMaxLength(100).IsRequired();
                entity.Property(e => e.DataChannelPeerId).HasMaxLength(500);
                entity.Property(e => e.UploadedAt).HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(e => e.Session)
                    .WithMany(s => s.Files)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.UploadedBy)
                    .WithMany(u => u.UploadedFiles)
                    .HasForeignKey(e => e.UploadedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
