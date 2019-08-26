using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Devart.Data.Oracle;
using HibernatingRhinos.Profiler.Appender.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace devart_efcore_value_conversion_bug_repro
{

    public class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                EntityFrameworkProfiler.Initialize();

                var config = Devart.Data.Oracle.Entity.Configuration.OracleEntityProviderConfig.Instance;
                config.CodeFirstOptions.UseNonUnicodeStrings = true;
                config.CodeFirstOptions.UseNonLobStrings = true;

                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.development.json", optional: false, reloadOnChange: true);
                var configuration = builder.Build();
                EntityContext.ConnectionString = ComposeConnectionString(configuration);

                using (var scope = new TransactionScope(
                    TransactionScopeOption.Required,
                    new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
                    TransactionScopeAsyncFlowOption.Enabled))
                {
                    using (var context = new EntityContext())
                    {
                        context.Database.EnsureDeleted();
                        context.Database.ExecuteSqlCommand(@"
CREATE TABLE CLASSROOM
(
    ID          NUMBER (19, 0) GENERATED ALWAYS AS IDENTITY NOT NULL,
    NAME        VARCHAR2(50 CHAR)
)");

                        context.Database.ExecuteSqlCommand(@"
CREATE TABLE PUZZLE_GROUP
(
    ID            NUMBER (19, 0) GENERATED ALWAYS AS IDENTITY NOT NULL,
    NAME        VARCHAR2(50 CHAR),
    CLASSROOM_ID  NUMBER (19, 0) NOT NULL
)");

                        context.Database.ExecuteSqlCommand(@"
CREATE TABLE PUZZLE
(
    ID              NUMBER (19, 0) GENERATED ALWAYS AS IDENTITY NOT NULL,
    PUZZLE_GROUP_ID NUMBER (19, 0) NOT NULL,
    COMPLETED       NUMBER (1, 0) NOT NULL
)");

                        var classroom = new Classroom("test");
                        var puzzleGroup1 = new PuzzleGroup(classroom, "group1");
                        var puzzleGroup2 = new PuzzleGroup(classroom, "group2");

                        var puzzle1 = new Puzzle(puzzleGroup1, true);
                        var puzzle2 = new Puzzle(puzzleGroup1, true);
                        var puzzle3 = new Puzzle(puzzleGroup2, true);
                        var puzzle4 = new Puzzle(puzzleGroup2, false);

                        context.Add(classroom);
                        context.Add(puzzleGroup1);
                        context.Add(puzzleGroup2);
                        context.Add(puzzle1);
                        context.Add(puzzle2);
                        context.Add(puzzle3);
                        context.Add(puzzle4);
                        await context.SaveChangesAsync();
                    }

                    scope.Complete();
                }

                using (var context = new EntityContext())
                {
                    // This works
                    var classroomResult1 = context
                        .Set<Classroom>()
                        .FirstOrDefault(_ => _.PuzzleGroups.Any(gr => gr.Puzzles.Any(p => p.Completed)));

                    // This doesn't - throws ORA-00936: missing expression
                    var classroomResult2 = context
                        .Set<Classroom>()
                        .FirstOrDefault(_ => _.PuzzleGroups.All(gr => gr.Puzzles.All(p => p.Completed)));
                }

                Console.WriteLine("Finished.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            Console.ReadKey();
        }

        private static string ComposeConnectionString(IConfiguration configuration)
        {
            var builder = new OracleConnectionStringBuilder
            {
                Server = configuration["DatabaseServer"],
                UserId = configuration["UserId"],
                Password = configuration["Password"],
                ServiceName = configuration["ServiceName"],
                Port = int.Parse(configuration["Port"]),
                Direct = true,
                Pooling = true,
                LicenseKey = configuration["DevartLicenseKey"]
            };
            return builder.ToString();
        }
    }

    public class EntityContext : DbContext
    {
        public static string ConnectionString;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseOracle(ConnectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Classroom>().ToTable("CLASSROOM");
            modelBuilder.Entity<Classroom>().HasKey(_ => _.Id);
            modelBuilder.Entity<Classroom>().Property(_ => _.Id).HasColumnName("ID");
            modelBuilder.Entity<Classroom>().Property(_ => _.Name).HasColumnName("NAME");
            modelBuilder.Entity<Classroom>().HasMany(_ => _.PuzzleGroups).WithOne(_ => _.Classroom).HasForeignKey("CLASSROOM_ID");

            modelBuilder.Entity<PuzzleGroup>().ToTable("PUZZLE_GROUP");
            modelBuilder.Entity<PuzzleGroup>().HasKey(_ => _.Id);
            modelBuilder.Entity<PuzzleGroup>().Property(_ => _.Id).HasColumnName("ID");
            modelBuilder.Entity<PuzzleGroup>().Property(_ => _.Name).HasColumnName("NAME");
            modelBuilder.Entity<PuzzleGroup>().HasMany(_ => _.Puzzles).WithOne(_ => _.PuzzleGroup).HasForeignKey("PUZZLE_GROUP_ID");

            modelBuilder.Entity<Puzzle>().ToTable("PUZZLE");
            modelBuilder.Entity<Puzzle>().HasKey(_ => _.Id);
            modelBuilder.Entity<Puzzle>().Property(_ => _.Id).HasColumnName("ID");
            modelBuilder.Entity<Puzzle>().Property(_ => _.Completed).HasColumnName("COMPLETED");
        }
    }

    public class Classroom
    {
        public long Id { get; private set; }
        public string Name { get; private set; }
        public List<PuzzleGroup> PuzzleGroups { get; private set; }

        private Classroom()
        {
            // Required by EF Core
        }

        public Classroom(string name)
        {
            Name = name;
        }
    }

    public class PuzzleGroup
    {
        public long Id { get; private set; }
        public string Name { get; private set; }
        public List<Puzzle> Puzzles { get; private set; }
        public Classroom Classroom { get; private set; }

        private PuzzleGroup()
        {
            // Required by EF Core
        }

        public PuzzleGroup(Classroom classroom, string name)
        {
            Classroom = classroom;
            Name = name;
        }
    }

    public class Puzzle
    {
        public long Id { get; private set; }
        public bool Completed { get; private set; }
        public PuzzleGroup PuzzleGroup { get; private set; }

        private Puzzle()
        {
            // Required by EF Core
        }

        public Puzzle(PuzzleGroup puzzleGroup, bool completed)
        {
            PuzzleGroup = puzzleGroup;
            Completed = completed;
        }
    }
}
