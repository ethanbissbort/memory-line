using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoryTimeline.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Note: This migration is manually created to match the Electron database schema
            // When running on Windows with .NET SDK, regenerate with: dotnet ef migrations add InitialCreate --force

            migrationBuilder.CreateTable(
                name: "eras",
                columns: table => new
                {
                    era_id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    start_date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    end_date = table.Column<DateTime>(type: "TEXT", nullable: true),
                    color_code = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eras", x => x.era_id);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    tag_id = table.Column<string>(type: "TEXT", nullable: false),
                    tag_name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.tag_id);
                });

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    person_id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_people", x => x.person_id);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    location_id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_locations", x => x.location_id);
                });

            migrationBuilder.CreateTable(
                name: "recording_queue",
                columns: table => new
                {
                    queue_id = table.Column<string>(type: "TEXT", nullable: false),
                    audio_file_path = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    duration_seconds = table.Column<double>(type: "REAL", nullable: true),
                    file_size_bytes = table.Column<long>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    processed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    error_message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recording_queue", x => x.queue_id);
                });

            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    setting_key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    setting_value = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_settings", x => x.setting_key);
                });

            migrationBuilder.CreateTable(
                name: "cross_references",
                columns: table => new
                {
                    reference_id = table.Column<string>(type: "TEXT", nullable: false),
                    event_id_1 = table.Column<string>(type: "TEXT", nullable: false),
                    event_id_2 = table.Column<string>(type: "TEXT", nullable: false),
                    relationship_type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    confidence_score = table.Column<double>(type: "REAL", nullable: true),
                    analysis_details = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cross_references", x => x.reference_id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    start_date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    end_date = table.Column<DateTime>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    era_id = table.Column<string>(type: "TEXT", nullable: true),
                    audio_file_path = table.Column<string>(type: "TEXT", nullable: true),
                    raw_transcript = table.Column<string>(type: "TEXT", nullable: true),
                    extraction_metadata = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.event_id);
                    table.ForeignKey(
                        name: "FK_events_eras_era_id",
                        column: x => x.era_id,
                        principalTable: "eras",
                        principalColumn: "era_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "pending_events",
                columns: table => new
                {
                    pending_id = table.Column<string>(type: "TEXT", nullable: false),
                    extracted_data = table.Column<string>(type: "TEXT", nullable: false),
                    audio_file_path = table.Column<string>(type: "TEXT", nullable: true),
                    transcript = table.Column<string>(type: "TEXT", nullable: true),
                    queue_id = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_events", x => x.pending_id);
                    table.ForeignKey(
                        name: "FK_pending_events_recording_queue_queue_id",
                        column: x => x.queue_id,
                        principalTable: "recording_queue",
                        principalColumn: "queue_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "event_embeddings",
                columns: table => new
                {
                    embedding_id = table.Column<string>(type: "TEXT", nullable: false),
                    event_id = table.Column<string>(type: "TEXT", nullable: false),
                    embedding_vector = table.Column<string>(type: "TEXT", nullable: false),
                    embedding_provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    embedding_model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    embedding_dimension = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_embeddings", x => x.embedding_id);
                    table.ForeignKey(
                        name: "FK_event_embeddings_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_tags",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "TEXT", nullable: false),
                    tag_id = table.Column<string>(type: "TEXT", nullable: false),
                    confidence_score = table.Column<double>(type: "REAL", nullable: false),
                    is_manual = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_tags", x => new { x.event_id, x.tag_id });
                    table.ForeignKey(
                        name: "FK_event_tags_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_tags_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "tag_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_people",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "TEXT", nullable: false),
                    person_id = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_people", x => new { x.event_id, x.person_id });
                    table.ForeignKey(
                        name: "FK_event_people_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_people_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "person_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_locations",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "TEXT", nullable: false),
                    location_id = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_locations", x => new { x.event_id, x.location_id });
                    table.ForeignKey(
                        name: "FK_event_locations_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_locations_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Create indexes
            migrationBuilder.CreateIndex(name: "IX_eras_name", table: "eras", column: "name", unique: true);
            migrationBuilder.CreateIndex(name: "IX_eras_dates", table: "eras", columns: new[] { "start_date", "end_date" });

            migrationBuilder.CreateIndex(name: "IX_tags_name", table: "tags", column: "tag_name", unique: true);

            migrationBuilder.CreateIndex(name: "IX_people_name", table: "people", column: "name", unique: true);

            migrationBuilder.CreateIndex(name: "IX_locations_name", table: "locations", column: "name", unique: true);

            migrationBuilder.CreateIndex(name: "IX_recording_queue_status", table: "recording_queue", column: "status");
            migrationBuilder.CreateIndex(name: "IX_recording_queue_created_at", table: "recording_queue", column: "created_at");

            migrationBuilder.CreateIndex(name: "IX_pending_events_status", table: "pending_events", column: "status");
            migrationBuilder.CreateIndex(name: "IX_pending_events_created_at", table: "pending_events", column: "created_at");
            migrationBuilder.CreateIndex(name: "IX_pending_events_queue_id", table: "pending_events", column: "queue_id");

            migrationBuilder.CreateIndex(name: "IX_cross_references_event_id_1", table: "cross_references", column: "event_id_1");
            migrationBuilder.CreateIndex(name: "IX_cross_references_event_id_2", table: "cross_references", column: "event_id_2");
            migrationBuilder.CreateIndex(name: "IX_cross_references_relationship_type", table: "cross_references", column: "relationship_type");

            migrationBuilder.CreateIndex(name: "IX_events_start_date", table: "events", column: "start_date");
            migrationBuilder.CreateIndex(name: "IX_events_end_date", table: "events", column: "end_date");
            migrationBuilder.CreateIndex(name: "IX_events_category", table: "events", column: "category");
            migrationBuilder.CreateIndex(name: "IX_events_era_id", table: "events", column: "era_id");

            migrationBuilder.CreateIndex(name: "IX_event_embeddings_event_id", table: "event_embeddings", column: "event_id", unique: true);
            migrationBuilder.CreateIndex(name: "IX_event_embeddings_provider", table: "event_embeddings", column: "embedding_provider");

            migrationBuilder.CreateIndex(name: "IX_event_tags_event_id", table: "event_tags", column: "event_id");
            migrationBuilder.CreateIndex(name: "IX_event_tags_tag_id", table: "event_tags", column: "tag_id");

            migrationBuilder.CreateIndex(name: "IX_event_people_event_id", table: "event_people", column: "event_id");
            migrationBuilder.CreateIndex(name: "IX_event_people_person_id", table: "event_people", column: "person_id");

            migrationBuilder.CreateIndex(name: "IX_event_locations_event_id", table: "event_locations", column: "event_id");
            migrationBuilder.CreateIndex(name: "IX_event_locations_location_id", table: "event_locations", column: "location_id");

            // Insert default settings
            migrationBuilder.InsertData(
                table: "app_settings",
                columns: new[] { "setting_key", "setting_value", "updated_at" },
                values: new object[,]
                {
                    { "theme", "light", DateTime.UtcNow },
                    { "default_zoom_level", "month", DateTime.UtcNow },
                    { "audio_quality", "high", DateTime.UtcNow },
                    { "llm_provider", "anthropic", DateTime.UtcNow },
                    { "llm_model", "claude-sonnet-4-20250514", DateTime.UtcNow },
                    { "llm_max_tokens", "4000", DateTime.UtcNow },
                    { "llm_temperature", "0.3", DateTime.UtcNow },
                    { "stt_engine", "windows", DateTime.UtcNow },
                    { "stt_config", "{}", DateTime.UtcNow },
                    { "rag_auto_run_enabled", "false", DateTime.UtcNow },
                    { "rag_schedule", "weekly", DateTime.UtcNow },
                    { "rag_similarity_threshold", "0.75", DateTime.UtcNow },
                    { "embedding_provider", "local", DateTime.UtcNow },
                    { "embedding_model", "onnx-text-embedding", DateTime.UtcNow },
                    { "embedding_api_key", "", DateTime.UtcNow },
                    { "auto_generate_embeddings", "true", DateTime.UtcNow },
                    { "send_transcripts_only", "true", DateTime.UtcNow },
                    { "require_confirmation", "true", DateTime.UtcNow }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "event_embeddings");
            migrationBuilder.DropTable(name: "event_tags");
            migrationBuilder.DropTable(name: "event_people");
            migrationBuilder.DropTable(name: "event_locations");
            migrationBuilder.DropTable(name: "pending_events");
            migrationBuilder.DropTable(name: "cross_references");
            migrationBuilder.DropTable(name: "app_settings");
            migrationBuilder.DropTable(name: "events");
            migrationBuilder.DropTable(name: "tags");
            migrationBuilder.DropTable(name: "people");
            migrationBuilder.DropTable(name: "locations");
            migrationBuilder.DropTable(name: "recording_queue");
            migrationBuilder.DropTable(name: "eras");
        }
    }
}
