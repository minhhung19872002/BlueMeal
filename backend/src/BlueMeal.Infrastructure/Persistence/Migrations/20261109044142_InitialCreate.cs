using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BlueMeal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.CreateTable(
                name: "alert",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    message_vi = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    message_en = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    link = table.Column<string>(type: "text", nullable: true),
                    dedupe_key = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_note = table.Column<string>(type: "text", nullable: true),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "api_client",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "text", nullable: false),
                    secret_hash = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    scopes = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_client", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "app_role",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_vi = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    requires_mfa = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "app_setting",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_setting", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "app_user",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    mfa_required = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_secret = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    email_otp_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    theme = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "backup_job",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    sha256 = table.Column<string>(type: "text", nullable: true),
                    location = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backup_job", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "country",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    iso2 = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_country", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_contact",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    email = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_contact", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_price",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    meal_shift_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    price_per_serving = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_price", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_type",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_type", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "department",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_department", x => x.id);
                    table.ForeignKey(
                        name: "fk_department_department_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "app",
                        principalTable: "department",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dish",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dish_type = table.Column<int>(type: "integer", nullable: false),
                    cooking_method = table.Column<int>(type: "integer", nullable: false),
                    serving_weight_g = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    image_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tags = table.Column<string>(type: "text", nullable: true),
                    allergens = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dish", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "doc_counter",
                schema: "app",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doc_counter", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "export_job",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_code = table.Column<string>(type: "text", nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    file_path = table.Column<string>(type: "text", nullable: true),
                    file_name = table.Column<string>(type: "text", nullable: true),
                    download_token = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    language = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_job", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "file_attachment",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_name = table.Column<string>(type: "text", nullable: false),
                    stored_name = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    owner_type = table.Column<string>(type: "text", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_attachment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "food_group",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_animal_source = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_food_group", x => x.id);
                    table.ForeignKey(
                        name: "fk_food_group_food_group_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "app",
                        principalTable: "food_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "food_safety_spec",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    food_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sensory_color = table.Column<string>(type: "text", nullable: true),
                    sensory_smell = table.Column<string>(type: "text", nullable: true),
                    sensory_state = table.Column<string>(type: "text", nullable: true),
                    storage_temp_min = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    storage_temp_max = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    max_shelf_days_after_receipt = table.Column<int>(type: "integer", nullable: true),
                    required_documents = table.Column<string>(type: "text", nullable: true),
                    allergens = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_food_safety_spec", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ingredient_price_history",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    source_doc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingredient_price_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_endpoint",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_url = table.Column<string>(type: "text", nullable: false),
                    auth_type = table.Column<int>(type: "integer", nullable: false),
                    credentials = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    sync_cron = table.Column<string>(type: "text", nullable: true),
                    mapping_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_endpoint", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_log",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: true),
                    direction = table.Column<string>(type: "text", nullable: false),
                    operation = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    records_received = table.Column<int>(type: "integer", nullable: false),
                    records_created = table.Column<int>(type: "integer", nullable: false),
                    records_updated = table.Column<int>(type: "integer", nullable: false),
                    conflicts = table.Column<int>(type: "integer", nullable: false),
                    request_summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    response_summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    triggered_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "login_history",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    result = table.Column<int>(type: "integer", nullable: false),
                    ip = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "meal_shift",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serve_from = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    serve_to = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meal_shift", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "meal_structure_template",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meal_structure_template", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "menu_approval",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "text", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    from_status = table.Column<int>(type: "integer", nullable: false),
                    to_status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_approval", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "menu_plan",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recommendation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    structure_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    meal_shift_ids = table.Column<string>(type: "text", nullable: true),
                    budget_per_serving = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    expected_servings_per_day = table.Column<int>(type: "integer", nullable: true),
                    no_repeat_days = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_plan", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mfa_recovery_code",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mfa_recovery_code", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    title_vi = table.Column<string>(type: "text", nullable: false),
                    title_en = table.Column<string>(type: "text", nullable: true),
                    body_vi = table.Column<string>(type: "text", nullable: true),
                    body_en = table.Column<string>(type: "text", nullable: true),
                    link = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nutrient",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit = table.Column<string>(type: "text", nullable: false),
                    group = table.Column<int>(type: "integer", nullable: false),
                    is_core = table.Column<bool>(type: "boolean", nullable: false),
                    decimals = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nutrient", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nutrient_retention_factor",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nutrient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cooking_method = table.Column<int>(type: "integer", nullable: false),
                    factor = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    source = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nutrient_retention_factor", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "one_time_token",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_one_time_token", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "organization",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: true),
                    tax_code = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    representative = table.Column<string>(type: "text", nullable: true),
                    representative_title = table.Column<string>(type: "text", nullable: true),
                    website = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "partner_rating",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rated_date = table.Column<DateOnly>(type: "date", nullable: false),
                    quality = table.Column<int>(type: "integer", nullable: false),
                    delivery = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_rating", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "partner_type",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_type", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "password_history",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permission",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    module = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    name_vi = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_request",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_no = table.Column<string>(type: "text", nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    need_date = table.Column<DateOnly>(type: "date", nullable: true),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    menu_ids = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    estimated_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_request", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report_group",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    required_permission = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_group", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_ledger",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    balance_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    balance_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    doc_type = table.Column<string>(type: "text", nullable: false),
                    doc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_no = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_ledger", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_period_lock",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_period_lock", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subject_group",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gender = table.Column<int>(type: "integer", nullable: false),
                    age_from = table.Column<int>(type: "integer", nullable: true),
                    age_to = table.Column<int>(type: "integer", nullable: true),
                    labor_intensity = table.Column<int>(type: "integer", nullable: false),
                    special_conditions = table.Column<string>(type: "text", nullable: true),
                    allergens = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    default_recommendation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subject_group", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_conflict",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_code = table.Column<string>(type: "text", nullable: false),
                    local_json = table.Column<string>(type: "jsonb", nullable: false),
                    remote_json = table.Column<string>(type: "jsonb", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_conflict", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_version",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    release_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes_vi = table.Column<string>(type: "text", nullable: false),
                    notes_en = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_version", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unit_of_measure",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    factor_to_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    symbol = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_of_measure", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_data_scope",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_type = table.Column<int>(type: "integer", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_data_scope", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_preference",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_preference", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_session",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refresh_token_hash = table.Column<string>(type: "text", nullable: false),
                    previous_token_hash = table.Column<string>(type: "text", nullable: true),
                    ip = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    device = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "text", nullable: true),
                    is_mfa_setup_only = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_session", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "warehouse",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    location = table.Column<string>(type: "text", nullable: true),
                    keeper_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    temp_min = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    temp_max = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouse", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role_claim",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claim_app_role_role_id",
                        column: x => x.role_id,
                        principalSchema: "app",
                        principalTable: "app_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claim",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claim_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_login",
                schema: "app",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_login", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_login_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_role",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_role", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_role_app_role_role_id",
                        column: x => x.role_id,
                        principalSchema: "app",
                        principalTable: "app_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_role_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_token",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_token", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_token_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "province",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_province", x => x.id);
                    table.ForeignKey(
                        name: "fk_province_country_country_id",
                        column: x => x.country_id,
                        principalSchema: "app",
                        principalTable: "country",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_code = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    province_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ward_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_person = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    email = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    default_subject_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    worker_count = table.Column<int>(type: "integer", nullable: true),
                    agreed_price_per_serving = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    shifts = table.Column<string>(type: "text", nullable: true),
                    allergens = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_customer_type_customer_type_id",
                        column: x => x.customer_type_id,
                        principalSchema: "app",
                        principalTable: "customer_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "employee",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position = table.Column<string>(type: "text", nullable: true),
                    gender = table.Column<int>(type: "integer", nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    phone = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    phone_hash = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    email_hash = table.Column<string>(type: "text", nullable: true),
                    national_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    national_id_hash = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_anonymized = table.Column<bool>(type: "boolean", nullable: false),
                    search_text = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_department_department_id",
                        column: x => x.department_id,
                        principalSchema: "app",
                        principalTable: "department",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "food_inspection_step2",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dish_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inspect_date = table.Column<DateOnly>(type: "date", nullable: false),
                    hygiene = table.Column<string>(type: "text", nullable: true),
                    participants = table.Column<string>(type: "text", nullable: true),
                    cook_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cook_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criteria = table.Column<string>(type: "jsonb", nullable: true),
                    sensory_note = table.Column<string>(type: "text", nullable: true),
                    result = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "text", nullable: true),
                    inspector = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_food_inspection_step2", x => x.id);
                    table.ForeignKey(
                        name: "fk_food_inspection_step2_dish_dish_id",
                        column: x => x.dish_id,
                        principalSchema: "app",
                        principalTable: "dish",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "food_inspection_step3",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dish_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inspect_date = table.Column<DateOnly>(type: "date", nullable: false),
                    portion_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    temperature = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    utensils = table.Column<string>(type: "text", nullable: true),
                    criteria = table.Column<string>(type: "jsonb", nullable: true),
                    sensory_note = table.Column<string>(type: "text", nullable: true),
                    result = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "text", nullable: true),
                    inspector = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_food_inspection_step3", x => x.id);
                    table.ForeignKey(
                        name: "fk_food_inspection_step3_dish_dish_id",
                        column: x => x.dish_id,
                        principalSchema: "app",
                        principalTable: "dish",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "food_sample",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sampled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    weight_g = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    storage_temp = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    sampler = table.Column<string>(type: "text", nullable: false),
                    planned_dispose_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    disposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disposer = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_food_sample", x => x.id);
                    table.ForeignKey(
                        name: "fk_food_sample_dish_dish_id",
                        column: x => x.dish_id,
                        principalSchema: "app",
                        principalTable: "dish",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    base_servings = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    instructions = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    prep_time_minutes = table.Column<int>(type: "integer", nullable: true),
                    cooking_loss_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe", x => x.id);
                    table.ForeignKey(
                        name: "fk_recipe_dish_dish_id",
                        column: x => x.dish_id,
                        principalSchema: "app",
                        principalTable: "dish",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "food",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    food_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refuse_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    is_animal_source_override = table.Column<bool>(type: "boolean", nullable: true),
                    source = table.Column<int>(type: "integer", nullable: false),
                    source_version = table.Column<string>(type: "text", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    remote_hash = table.Column<string>(type: "text", nullable: true),
                    locally_modified = table.Column<bool>(type: "boolean", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_food", x => x.id);
                    table.ForeignKey(
                        name: "fk_food_food_group_food_group_id",
                        column: x => x.food_group_id,
                        principalSchema: "app",
                        principalTable: "food_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meal_structure_slot",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dish_type = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meal_structure_slot", x => x.id);
                    table.ForeignKey(
                        name: "fk_meal_structure_slot_meal_structure_template_template_id",
                        column: x => x.template_id,
                        principalSchema: "app",
                        principalTable: "meal_structure_template",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "partner",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_code = table.Column<string>(type: "text", nullable: true),
                    contact_person = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    email = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    province_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ward_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bank_account = table.Column<string>(type: "text", nullable: true),
                    bank_name = table.Column<string>(type: "text", nullable: true),
                    supplied_groups = table.Column<string>(type: "text", nullable: true),
                    rating = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner", x => x.id);
                    table.ForeignKey(
                        name: "fk_partner_partner_type_partner_type_id",
                        column: x => x.partner_type_id,
                        principalSchema: "app",
                        principalTable: "partner_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "role_permission",
                schema: "app",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permission", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_role_permission_permission_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "app",
                        principalTable: "permission",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_permission_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "app",
                        principalTable: "app_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_definition",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name_vi = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: false),
                    report_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_definition", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_definition_report_group_report_group_id",
                        column: x => x.report_group_id,
                        principalSchema: "app",
                        principalTable: "report_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "recommendation",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference = table.Column<string>(type: "text", nullable: true),
                    reference_year = table.Column<int>(type: "integer", nullable: true),
                    energy_per_day_kcal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    meal_share_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    protein_pct_min = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    protein_pct_max = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    lipid_pct_min = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    lipid_pct_max = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    glucid_pct_min = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    glucid_pct_max = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    animal_protein_pct_min = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    animal_protein_pct_max = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    budget_per_serving = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation", x => x.id);
                    table.ForeignKey(
                        name: "fk_recommendation_subject_group_subject_group_id",
                        column: x => x.subject_group_id,
                        principalSchema: "app",
                        principalTable: "subject_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "stock_issue",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_no = table.Column<string>(type: "text", nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_take_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_planned = table.Column<bool>(type: "boolean", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_issue", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_issue_warehouse_warehouse_id",
                        column: x => x.warehouse_id,
                        principalSchema: "app",
                        principalTable: "warehouse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_take",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_no = table.Column<string>(type: "text", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    take_date = table.Column<DateOnly>(type: "date", nullable: false),
                    members = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    adjust_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    adjust_issue_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_take", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_take_warehouse_warehouse_id",
                        column: x => x.warehouse_id,
                        principalSchema: "app",
                        principalTable: "warehouse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ward",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    province_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ward", x => x.id);
                    table.ForeignKey(
                        name: "fk_ward_province_province_id",
                        column: x => x.province_id,
                        principalSchema: "app",
                        principalTable: "province",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meal_order",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_date = table.Column<DateOnly>(type: "date", nullable: false),
                    meal_shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    registered_servings = table.Column<int>(type: "integer", nullable: false),
                    actual_servings = table.Column<int>(type: "integer", nullable: true),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meal_order", x => x.id);
                    table.ForeignKey(
                        name: "fk_meal_order_customer_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "app",
                        principalTable: "customer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    menu_date = table.Column<DateOnly>(type: "date", nullable: false),
                    meal_shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recommendation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    structure_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    servings = table.Column<int>(type: "integer", nullable: false),
                    budget_per_serving = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    creation_mode = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    approval_level = table.Column<int>(type: "integer", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    nutrition_score = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    cost_per_serving = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    energy_kcal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    critical_warnings = table.Column<int>(type: "integer", nullable: false),
                    critical_override_reason = table.Column<string>(type: "text", nullable: true),
                    critical_override_by = table.Column<Guid>(type: "uuid", nullable: true),
                    generator_explanation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu", x => x.id);
                    table.ForeignKey(
                        name: "fk_menu_customer_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "app",
                        principalTable: "customer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_menu_meal_shift_meal_shift_id",
                        column: x => x.meal_shift_id,
                        principalSchema: "app",
                        principalTable: "meal_shift",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_menu_subject_group_subject_group_id",
                        column: x => x.subject_group_id,
                        principalSchema: "app",
                        principalTable: "subject_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "food_nutrient",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    food_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nutrient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value_per100g_edible = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_food_nutrient", x => x.id);
                    table.ForeignKey(
                        name: "fk_food_nutrient_food_food_id",
                        column: x => x.food_id,
                        principalSchema: "app",
                        principalTable: "food",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_food_nutrient_nutrient_nutrient_id",
                        column: x => x.nutrient_id,
                        principalSchema: "app",
                        principalTable: "nutrient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ingredient",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    food_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purchase_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    grams_per_unit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    default_warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    min_stock = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    shelf_life_days = table.Column<int>(type: "integer", nullable: true),
                    storage_condition = table.Column<string>(type: "text", nullable: true),
                    reference_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    preferred_partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allergens = table.Column<string>(type: "text", nullable: true),
                    barcode = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_vi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    search_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingredient", x => x.id);
                    table.ForeignKey(
                        name: "fk_ingredient_food_food_id",
                        column: x => x.food_id,
                        principalSchema: "app",
                        principalTable: "food",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ingredient_unit_of_measure_purchase_unit_id",
                        column: x => x.purchase_unit_id,
                        principalSchema: "app",
                        principalTable: "unit_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "partner_certificate",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    certificate_type = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    issuer = table.Column<string>(type: "text", nullable: true),
                    issued_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_certificate", x => x.id);
                    table.ForeignKey(
                        name: "fk_partner_certificate_partner_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "app",
                        principalTable: "partner",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "partner_contract",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_no = table.Column<string>(type: "text", nullable: false),
                    signed_date = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_contract", x => x.id);
                    table.ForeignKey(
                        name: "fk_partner_contract_partner_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "app",
                        principalTable: "partner",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_receipt",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_no = table.Column<string>(type: "text", nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purchase_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_no = table.Column<string>(type: "text", nullable: true),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_receipt", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_receipt_partner_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "app",
                        principalTable: "partner",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_receipt_warehouse_warehouse_id",
                        column: x => x.warehouse_id,
                        principalSchema: "app",
                        principalTable: "warehouse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_nutrient",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recommendation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nutrient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_per_meal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    max_per_meal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    level = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation_nutrient", x => x.id);
                    table.ForeignKey(
                        name: "fk_recommendation_nutrient_nutrient_nutrient_id",
                        column: x => x.nutrient_id,
                        principalSchema: "app",
                        principalTable: "nutrient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recommendation_nutrient_recommendation_recommendation_id",
                        column: x => x.recommendation_id,
                        principalSchema: "app",
                        principalTable: "recommendation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_item",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: true),
                    serving_factor = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_menu_item_dish_dish_id",
                        column: x => x.dish_id,
                        principalSchema: "app",
                        principalTable: "dish",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_menu_item_menu_menu_id",
                        column: x => x.menu_id,
                        principalSchema: "app",
                        principalTable: "menu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "food_inspection_step1",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inspected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receipt_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    lot_no = table.Column<string>(type: "text", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    criteria = table.Column<string>(type: "jsonb", nullable: true),
                    sensory_note = table.Column<string>(type: "text", nullable: true),
                    origin_docs_ok = table.Column<bool>(type: "boolean", nullable: false),
                    origin_docs = table.Column<string>(type: "text", nullable: true),
                    temperature = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    result = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "text", nullable: true),
                    inspector = table.Column<string>(type: "text", nullable: false),
                    inspector_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_food_inspection_step1", x => x.id);
                    table.ForeignKey(
                        name: "fk_food_inspection_step1_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "app",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_request_line",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    required_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    available_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    min_stock = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    suggested_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    order_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    estimated_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_request_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_request_line_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "app",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_request_line_purchase_request_request_id",
                        column: x => x.request_id,
                        principalSchema: "app",
                        principalTable: "purchase_request",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recipe_item",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gross_g = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    net_g_override = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    prep_note = table.Column<string>(type: "text", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_recipe_item_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "app",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recipe_item_recipe_recipe_id",
                        column: x => x.recipe_id,
                        principalSchema: "app",
                        principalTable: "recipe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_balance",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    average_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_balance", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_balance_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "app",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_issue_line",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_no = table.Column<string>(type: "text", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_issue_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_issue_line_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "app",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_issue_line_stock_issue_issue_id",
                        column: x => x.issue_id,
                        principalSchema: "app",
                        principalTable: "stock_issue",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_lot",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_no = table.Column<string>(type: "text", nullable: true),
                    mfg_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    received_date = table.Column<DateOnly>(type: "date", nullable: false),
                    receipt_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    initial_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_lot", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_lot_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "app",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_take_line",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_take_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_no = table.Column<string>(type: "text", nullable: true),
                    book_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    actual_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cause = table.Column<string>(type: "text", nullable: true),
                    resolution = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_take_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_take_line_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "app",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_take_line_stock_take_stock_take_id",
                        column: x => x.stock_take_id,
                        principalSchema: "app",
                        principalTable: "stock_take",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "partner_contract_price",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_contract_price", x => x.id);
                    table.ForeignKey(
                        name: "fk_partner_contract_price_partner_contract_contract_id",
                        column: x => x.contract_id,
                        principalSchema: "app",
                        principalTable: "partner_contract",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_receipt_line",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    lot_no = table.Column<string>(type: "text", nullable: true),
                    mfg_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    quality_result = table.Column<int>(type: "integer", nullable: true),
                    received_temp = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    photo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_receipt_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_receipt_line_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "app",
                        principalTable: "ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_receipt_line_stock_receipt_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "app",
                        principalTable: "stock_receipt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alert_dedupe_key",
                schema: "app",
                table: "alert",
                column: "dedupe_key");

            migrationBuilder.CreateIndex(
                name: "ix_alert_status_level_created_at",
                schema: "app",
                table: "alert",
                columns: new[] { "status", "level", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_api_client_client_id",
                schema: "app",
                table: "api_client",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "app",
                table: "app_role",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_app_setting_key",
                schema: "app",
                table: "app_setting",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "app",
                table: "app_user",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "app",
                table: "app_user",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_backup_job_requested_at",
                schema: "app",
                table: "backup_job",
                column: "requested_at");

            migrationBuilder.CreateIndex(
                name: "ix_country_code",
                schema: "app",
                table: "country",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_country_search_text",
                schema: "app",
                table: "country",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_code",
                schema: "app",
                table: "customer",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_customer_customer_type_id",
                schema: "app",
                table: "customer",
                column: "customer_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_search_text",
                schema: "app",
                table: "customer",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_contact_customer_id",
                schema: "app",
                table: "customer_contact",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_price_customer_id_valid_from",
                schema: "app",
                table: "customer_price",
                columns: new[] { "customer_id", "valid_from" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_type_code",
                schema: "app",
                table: "customer_type",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_customer_type_search_text",
                schema: "app",
                table: "customer_type",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_department_code",
                schema: "app",
                table: "department",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_department_parent_id",
                schema: "app",
                table: "department",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_department_search_text",
                schema: "app",
                table: "department",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_dish_code",
                schema: "app",
                table: "dish",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_dish_search_text",
                schema: "app",
                table: "dish",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_code",
                schema: "app",
                table: "employee",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_employee_department_id",
                schema: "app",
                table: "employee",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_email_hash",
                schema: "app",
                table: "employee",
                column: "email_hash");

            migrationBuilder.CreateIndex(
                name: "ix_employee_national_id_hash",
                schema: "app",
                table: "employee",
                column: "national_id_hash");

            migrationBuilder.CreateIndex(
                name: "ix_employee_phone_hash",
                schema: "app",
                table: "employee",
                column: "phone_hash");

            migrationBuilder.CreateIndex(
                name: "ix_employee_search_text",
                schema: "app",
                table: "employee",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_export_job_download_token",
                schema: "app",
                table: "export_job",
                column: "download_token");

            migrationBuilder.CreateIndex(
                name: "ix_export_job_requested_by_requested_at",
                schema: "app",
                table: "export_job",
                columns: new[] { "requested_by", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_file_attachment_owner_type_owner_id",
                schema: "app",
                table: "file_attachment",
                columns: new[] { "owner_type", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_food_code",
                schema: "app",
                table: "food",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_food_food_group_id",
                schema: "app",
                table: "food",
                column: "food_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_food_search_text",
                schema: "app",
                table: "food",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_food_group_code",
                schema: "app",
                table: "food_group",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_food_group_parent_id",
                schema: "app",
                table: "food_group",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_food_group_search_text",
                schema: "app",
                table: "food_group",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_food_inspection_step1_ingredient_id",
                schema: "app",
                table: "food_inspection_step1",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_food_inspection_step1_inspected_at",
                schema: "app",
                table: "food_inspection_step1",
                column: "inspected_at");

            migrationBuilder.CreateIndex(
                name: "ix_food_inspection_step1_receipt_id",
                schema: "app",
                table: "food_inspection_step1",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_food_inspection_step2_dish_id",
                schema: "app",
                table: "food_inspection_step2",
                column: "dish_id");

            migrationBuilder.CreateIndex(
                name: "ix_food_inspection_step2_inspect_date",
                schema: "app",
                table: "food_inspection_step2",
                column: "inspect_date");

            migrationBuilder.CreateIndex(
                name: "ix_food_inspection_step3_dish_id",
                schema: "app",
                table: "food_inspection_step3",
                column: "dish_id");

            migrationBuilder.CreateIndex(
                name: "ix_food_inspection_step3_inspect_date",
                schema: "app",
                table: "food_inspection_step3",
                column: "inspect_date");

            migrationBuilder.CreateIndex(
                name: "ix_food_nutrient_food_id_nutrient_id",
                schema: "app",
                table: "food_nutrient",
                columns: new[] { "food_id", "nutrient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_food_nutrient_nutrient_id",
                schema: "app",
                table: "food_nutrient",
                column: "nutrient_id");

            migrationBuilder.CreateIndex(
                name: "ix_food_safety_spec_food_id",
                schema: "app",
                table: "food_safety_spec",
                column: "food_id");

            migrationBuilder.CreateIndex(
                name: "ix_food_safety_spec_ingredient_id",
                schema: "app",
                table: "food_safety_spec",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_food_sample_code",
                schema: "app",
                table: "food_sample",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_food_sample_dish_id",
                schema: "app",
                table: "food_sample",
                column: "dish_id");

            migrationBuilder.CreateIndex(
                name: "ix_food_sample_disposed_at_planned_dispose_at",
                schema: "app",
                table: "food_sample",
                columns: new[] { "disposed_at", "planned_dispose_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_barcode",
                schema: "app",
                table: "ingredient",
                column: "barcode");

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_code",
                schema: "app",
                table: "ingredient",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_food_id",
                schema: "app",
                table: "ingredient",
                column: "food_id");

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_purchase_unit_id",
                schema: "app",
                table: "ingredient",
                column: "purchase_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_search_text",
                schema: "app",
                table: "ingredient",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_price_history_ingredient_id_effective_date",
                schema: "app",
                table: "ingredient_price_history",
                columns: new[] { "ingredient_id", "effective_date" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_endpoint_code",
                schema: "app",
                table: "integration_endpoint",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_integration_endpoint_search_text",
                schema: "app",
                table: "integration_endpoint",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_log_started_at",
                schema: "app",
                table: "integration_log",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_login_history_user_id_at",
                schema: "app",
                table: "login_history",
                columns: new[] { "user_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_meal_order_customer_id_order_date_meal_shift_id",
                schema: "app",
                table: "meal_order",
                columns: new[] { "customer_id", "order_date", "meal_shift_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meal_shift_code",
                schema: "app",
                table: "meal_shift",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_meal_shift_search_text",
                schema: "app",
                table: "meal_shift",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_meal_structure_slot_template_id",
                schema: "app",
                table: "meal_structure_slot",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_meal_structure_template_code",
                schema: "app",
                table: "meal_structure_template",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_meal_structure_template_search_text",
                schema: "app",
                table: "meal_structure_template",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_menu_code",
                schema: "app",
                table: "menu",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_menu_customer_id",
                schema: "app",
                table: "menu",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_meal_shift_id",
                schema: "app",
                table: "menu",
                column: "meal_shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_menu_date_meal_shift_id_subject_group_id_customer_id",
                schema: "app",
                table: "menu",
                columns: new[] { "menu_date", "meal_shift_id", "subject_group_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_menu_plan_id",
                schema: "app",
                table: "menu",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_status",
                schema: "app",
                table: "menu",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_menu_subject_group_id",
                schema: "app",
                table: "menu",
                column: "subject_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_approval_menu_id_at",
                schema: "app",
                table: "menu_approval",
                columns: new[] { "menu_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_menu_item_dish_id",
                schema: "app",
                table: "menu_item",
                column: "dish_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_item_menu_id",
                schema: "app",
                table: "menu_item",
                column: "menu_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_plan_from_date_to_date",
                schema: "app",
                table: "menu_plan",
                columns: new[] { "from_date", "to_date" });

            migrationBuilder.CreateIndex(
                name: "ix_mfa_recovery_code_user_id",
                schema: "app",
                table: "mfa_recovery_code",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_user_id_read_at_created_at",
                schema: "app",
                table: "notification",
                columns: new[] { "user_id", "read_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_nutrient_code",
                schema: "app",
                table: "nutrient",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_nutrient_search_text",
                schema: "app",
                table: "nutrient",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_nutrient_retention_factor_nutrient_id_cooking_method",
                schema: "app",
                table: "nutrient_retention_factor",
                columns: new[] { "nutrient_id", "cooking_method" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_one_time_token_token_hash",
                schema: "app",
                table: "one_time_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_partner_code",
                schema: "app",
                table: "partner",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_partner_partner_type_id",
                schema: "app",
                table: "partner",
                column: "partner_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_partner_search_text",
                schema: "app",
                table: "partner",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_partner_certificate_expiry_date",
                schema: "app",
                table: "partner_certificate",
                column: "expiry_date");

            migrationBuilder.CreateIndex(
                name: "ix_partner_certificate_partner_id",
                schema: "app",
                table: "partner_certificate",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_partner_contract_partner_id_contract_no",
                schema: "app",
                table: "partner_contract",
                columns: new[] { "partner_id", "contract_no" });

            migrationBuilder.CreateIndex(
                name: "ix_partner_contract_price_contract_id",
                schema: "app",
                table: "partner_contract_price",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_partner_rating_partner_id",
                schema: "app",
                table: "partner_rating",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_partner_type_code",
                schema: "app",
                table: "partner_type",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_partner_type_search_text",
                schema: "app",
                table: "partner_type",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_password_history_user_id_created_at",
                schema: "app",
                table: "password_history",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_permission_code",
                schema: "app",
                table: "permission",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_province_code",
                schema: "app",
                table: "province",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_province_country_id",
                schema: "app",
                table: "province",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "ix_province_search_text",
                schema: "app",
                table: "province",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_request_doc_no",
                schema: "app",
                table: "purchase_request",
                column: "doc_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_request_line_ingredient_id",
                schema: "app",
                table: "purchase_request_line",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_request_line_request_id",
                schema: "app",
                table: "purchase_request_line",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ix_recipe_dish_id_version_no",
                schema: "app",
                table: "recipe",
                columns: new[] { "dish_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recipe_item_ingredient_id",
                schema: "app",
                table: "recipe_item",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_recipe_item_recipe_id",
                schema: "app",
                table: "recipe_item",
                column: "recipe_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_code",
                schema: "app",
                table: "recommendation",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_search_text",
                schema: "app",
                table: "recommendation",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_subject_group_id",
                schema: "app",
                table: "recommendation",
                column: "subject_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_nutrient_nutrient_id",
                schema: "app",
                table: "recommendation_nutrient",
                column: "nutrient_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_nutrient_recommendation_id_nutrient_id",
                schema: "app",
                table: "recommendation_nutrient",
                columns: new[] { "recommendation_id", "nutrient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_definition_code",
                schema: "app",
                table: "report_definition",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_definition_report_group_id",
                schema: "app",
                table: "report_definition",
                column: "report_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_group_code",
                schema: "app",
                table: "report_group",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_report_group_search_text",
                schema: "app",
                table: "report_group",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_role_claim_role_id",
                schema: "app",
                table: "role_claim",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permission_permission_id",
                schema: "app",
                table: "role_permission",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_balance_ingredient_id",
                schema: "app",
                table: "stock_balance",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_balance_warehouse_id_ingredient_id",
                schema: "app",
                table: "stock_balance",
                columns: new[] { "warehouse_id", "ingredient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_issue_doc_no",
                schema: "app",
                table: "stock_issue",
                column: "doc_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_issue_menu_id",
                schema: "app",
                table: "stock_issue",
                column: "menu_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_issue_warehouse_id_doc_date",
                schema: "app",
                table: "stock_issue",
                columns: new[] { "warehouse_id", "doc_date" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_issue_line_ingredient_id",
                schema: "app",
                table: "stock_issue_line",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_issue_line_issue_id",
                schema: "app",
                table: "stock_issue_line",
                column: "issue_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_doc_date",
                schema: "app",
                table: "stock_ledger",
                column: "doc_date");

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_doc_type_doc_id",
                schema: "app",
                table: "stock_ledger",
                columns: new[] { "doc_type", "doc_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_warehouse_id_ingredient_id_at",
                schema: "app",
                table: "stock_ledger",
                columns: new[] { "warehouse_id", "ingredient_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_lot_ingredient_id",
                schema: "app",
                table: "stock_lot",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_lot_warehouse_id_ingredient_id_expiry_date",
                schema: "app",
                table: "stock_lot",
                columns: new[] { "warehouse_id", "ingredient_id", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_period_lock_warehouse_id_period",
                schema: "app",
                table: "stock_period_lock",
                columns: new[] { "warehouse_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_receipt_doc_no",
                schema: "app",
                table: "stock_receipt",
                column: "doc_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_receipt_partner_id",
                schema: "app",
                table: "stock_receipt",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_receipt_warehouse_id_doc_date",
                schema: "app",
                table: "stock_receipt",
                columns: new[] { "warehouse_id", "doc_date" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_receipt_line_ingredient_id",
                schema: "app",
                table: "stock_receipt_line",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_receipt_line_receipt_id",
                schema: "app",
                table: "stock_receipt_line",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_take_doc_no",
                schema: "app",
                table: "stock_take",
                column: "doc_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_take_warehouse_id",
                schema: "app",
                table: "stock_take",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_take_line_ingredient_id",
                schema: "app",
                table: "stock_take_line",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_take_line_stock_take_id",
                schema: "app",
                table: "stock_take_line",
                column: "stock_take_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_group_code",
                schema: "app",
                table: "subject_group",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_subject_group_search_text",
                schema: "app",
                table: "subject_group",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_conflict_status_detected_at",
                schema: "app",
                table: "sync_conflict",
                columns: new[] { "status", "detected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_system_version_version",
                schema: "app",
                table: "system_version",
                column: "version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unit_of_measure_code",
                schema: "app",
                table: "unit_of_measure",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_unit_of_measure_search_text",
                schema: "app",
                table: "unit_of_measure",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_user_claim_user_id",
                schema: "app",
                table: "user_claim",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_data_scope_user_id_scope_type_scope_id",
                schema: "app",
                table: "user_data_scope",
                columns: new[] { "user_id", "scope_type", "scope_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_login_user_id",
                schema: "app",
                table: "user_login",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_preference_user_id_key",
                schema: "app",
                table: "user_preference",
                columns: new[] { "user_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_role_role_id",
                schema: "app",
                table: "user_role",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_session_previous_token_hash",
                schema: "app",
                table: "user_session",
                column: "previous_token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_user_session_refresh_token_hash",
                schema: "app",
                table: "user_session",
                column: "refresh_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_session_user_id_revoked_at",
                schema: "app",
                table: "user_session",
                columns: new[] { "user_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ward_code",
                schema: "app",
                table: "ward",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_ward_province_id",
                schema: "app",
                table: "ward",
                column: "province_id");

            migrationBuilder.CreateIndex(
                name: "ix_ward_search_text",
                schema: "app",
                table: "ward",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_code",
                schema: "app",
                table: "warehouse",
                column: "code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_search_text",
                schema: "app",
                table: "warehouse",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        
            // ---- Append-only, monthly-partitioned audit log (SPEC §10.3, ASSUMPTIONS A-33)
            migrationBuilder.Sql(AuditLogSql.Create);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS app.ensure_audit_partitions(int); DROP TABLE IF EXISTS app.audit_log CASCADE;");

            migrationBuilder.DropTable(
                name: "alert",
                schema: "app");

            migrationBuilder.DropTable(
                name: "api_client",
                schema: "app");

            migrationBuilder.DropTable(
                name: "app_setting",
                schema: "app");

            migrationBuilder.DropTable(
                name: "backup_job",
                schema: "app");

            migrationBuilder.DropTable(
                name: "customer_contact",
                schema: "app");

            migrationBuilder.DropTable(
                name: "customer_price",
                schema: "app");

            migrationBuilder.DropTable(
                name: "doc_counter",
                schema: "app");

            migrationBuilder.DropTable(
                name: "employee",
                schema: "app");

            migrationBuilder.DropTable(
                name: "export_job",
                schema: "app");

            migrationBuilder.DropTable(
                name: "file_attachment",
                schema: "app");

            migrationBuilder.DropTable(
                name: "food_inspection_step1",
                schema: "app");

            migrationBuilder.DropTable(
                name: "food_inspection_step2",
                schema: "app");

            migrationBuilder.DropTable(
                name: "food_inspection_step3",
                schema: "app");

            migrationBuilder.DropTable(
                name: "food_nutrient",
                schema: "app");

            migrationBuilder.DropTable(
                name: "food_safety_spec",
                schema: "app");

            migrationBuilder.DropTable(
                name: "food_sample",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ingredient_price_history",
                schema: "app");

            migrationBuilder.DropTable(
                name: "integration_endpoint",
                schema: "app");

            migrationBuilder.DropTable(
                name: "integration_log",
                schema: "app");

            migrationBuilder.DropTable(
                name: "login_history",
                schema: "app");

            migrationBuilder.DropTable(
                name: "meal_order",
                schema: "app");

            migrationBuilder.DropTable(
                name: "meal_structure_slot",
                schema: "app");

            migrationBuilder.DropTable(
                name: "menu_approval",
                schema: "app");

            migrationBuilder.DropTable(
                name: "menu_item",
                schema: "app");

            migrationBuilder.DropTable(
                name: "menu_plan",
                schema: "app");

            migrationBuilder.DropTable(
                name: "mfa_recovery_code",
                schema: "app");

            migrationBuilder.DropTable(
                name: "notification",
                schema: "app");

            migrationBuilder.DropTable(
                name: "nutrient_retention_factor",
                schema: "app");

            migrationBuilder.DropTable(
                name: "one_time_token",
                schema: "app");

            migrationBuilder.DropTable(
                name: "organization",
                schema: "app");

            migrationBuilder.DropTable(
                name: "partner_certificate",
                schema: "app");

            migrationBuilder.DropTable(
                name: "partner_contract_price",
                schema: "app");

            migrationBuilder.DropTable(
                name: "partner_rating",
                schema: "app");

            migrationBuilder.DropTable(
                name: "password_history",
                schema: "app");

            migrationBuilder.DropTable(
                name: "purchase_request_line",
                schema: "app");

            migrationBuilder.DropTable(
                name: "recipe_item",
                schema: "app");

            migrationBuilder.DropTable(
                name: "recommendation_nutrient",
                schema: "app");

            migrationBuilder.DropTable(
                name: "report_definition",
                schema: "app");

            migrationBuilder.DropTable(
                name: "role_claim",
                schema: "app");

            migrationBuilder.DropTable(
                name: "role_permission",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stock_balance",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stock_issue_line",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stock_ledger",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stock_lot",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stock_period_lock",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stock_receipt_line",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stock_take_line",
                schema: "app");

            migrationBuilder.DropTable(
                name: "sync_conflict",
                schema: "app");

            migrationBuilder.DropTable(
                name: "system_version",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_claim",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_data_scope",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_login",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_preference",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_role",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_session",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_token",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ward",
                schema: "app");

            migrationBuilder.DropTable(
                name: "department",
                schema: "app");

            migrationBuilder.DropTable(
                name: "meal_structure_template",
                schema: "app");

            migrationBuilder.DropTable(
                name: "menu",
                schema: "app");

            migrationBuilder.DropTable(
                name: "partner_contract",
                schema: "app");

            migrationBuilder.DropTable(
                name: "purchase_request",
                schema: "app");

            migrationBuilder.DropTable(
                name: "recipe",
                schema: "app");

            migrationBuilder.DropTable(
                name: "nutrient",
                schema: "app");

            migrationBuilder.DropTable(
                name: "recommendation",
                schema: "app");

            migrationBuilder.DropTable(
                name: "report_group",
                schema: "app");

            migrationBuilder.DropTable(
                name: "permission",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stock_issue",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stock_receipt",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ingredient",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stock_take",
                schema: "app");

            migrationBuilder.DropTable(
                name: "app_role",
                schema: "app");

            migrationBuilder.DropTable(
                name: "app_user",
                schema: "app");

            migrationBuilder.DropTable(
                name: "province",
                schema: "app");

            migrationBuilder.DropTable(
                name: "customer",
                schema: "app");

            migrationBuilder.DropTable(
                name: "meal_shift",
                schema: "app");

            migrationBuilder.DropTable(
                name: "dish",
                schema: "app");

            migrationBuilder.DropTable(
                name: "subject_group",
                schema: "app");

            migrationBuilder.DropTable(
                name: "partner",
                schema: "app");

            migrationBuilder.DropTable(
                name: "food",
                schema: "app");

            migrationBuilder.DropTable(
                name: "unit_of_measure",
                schema: "app");

            migrationBuilder.DropTable(
                name: "warehouse",
                schema: "app");

            migrationBuilder.DropTable(
                name: "country",
                schema: "app");

            migrationBuilder.DropTable(
                name: "customer_type",
                schema: "app");

            migrationBuilder.DropTable(
                name: "partner_type",
                schema: "app");

            migrationBuilder.DropTable(
                name: "food_group",
                schema: "app");
        }
    }
}
