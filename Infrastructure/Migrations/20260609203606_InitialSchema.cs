using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    priority = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_retries = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recurrence = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locked_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dead_letter_queue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    error_details = table.Column<string>(type: "text", nullable: false),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dead_letter_queue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dead_letter_queue_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_dependencies",
                columns: table => new
                {
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depends_on_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_dependencies", x => new { x.job_id, x.depends_on_id });
                    table.ForeignKey(
                        name: "FK_job_dependencies_jobs_depends_on_id",
                        column: x => x.depends_on_id,
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_job_dependencies_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @event = table.Column<string>(name: "event", type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_job_logs_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_dlq_resolved",
                table: "dead_letter_queue",
                columns: new[] { "resolved", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_dead_letter_queue_job_id",
                table: "dead_letter_queue",
                column: "job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_job_dependencies_depends_on",
                table: "job_dependencies",
                column: "depends_on_id");

            migrationBuilder.CreateIndex(
                name: "idx_job_logs_job_id_created",
                table: "job_logs",
                columns: new[] { "job_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_jobs_locked_at",
                table: "jobs",
                column: "locked_at",
                filter: "locked_by IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_jobs_priority_scheduled_created",
                table: "jobs",
                columns: new[] { "priority", "scheduled_at", "created_at" },
                filter: "status = 'Pending' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_jobs_scheduled_pending",
                table: "jobs",
                column: "scheduled_at",
                filter: "status = 'Pending' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_jobs_status_scheduled_at",
                table: "jobs",
                columns: new[] { "status", "scheduled_at" },
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dead_letter_queue");

            migrationBuilder.DropTable(
                name: "job_dependencies");

            migrationBuilder.DropTable(
                name: "job_logs");

            migrationBuilder.DropTable(
                name: "jobs");
        }
    }
}
