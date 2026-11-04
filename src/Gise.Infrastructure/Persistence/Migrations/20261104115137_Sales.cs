using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gise.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "queue_sequence");

            migrationBuilder.CreateTable(
                name: "holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    places = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holds", x => x.id);
                    table.ForeignKey(
                        name: "fk_holds_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_holds_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "processed_notices",
                columns: table => new
                {
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notice_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_notices", x => new { x.provider, x.notice_id });
                });

            migrationBuilder.CreateTable(
                name: "queue_entries",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_key = table.Column<double>(type: "double precision", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('queue_sequence')"),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    admitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_queue_entries", x => new { x.event_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_queue_entries_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_queue_entries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hold_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payment_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payment_provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    payment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    checkout_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refund_reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    refund_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lines = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_orders_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_holds_hold_id",
                        column: x => x.hold_id,
                        principalTable: "holds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_holds_event_id_user_id_status",
                table: "holds",
                columns: new[] { "event_id", "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_holds_expires_at",
                table: "holds",
                column: "expires_at",
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_holds_user_id",
                table: "holds",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_event_id_status",
                table: "orders",
                columns: new[] { "event_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_hold_id",
                table: "orders",
                column: "hold_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_number",
                table: "orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_payment_deadline",
                table: "orders",
                column: "payment_deadline",
                filter: "status = 'PendingPayment'");

            migrationBuilder.CreateIndex(
                name: "ix_orders_refund_requested_at",
                table: "orders",
                column: "refund_requested_at",
                filter: "status = 'RefundPending'");

            migrationBuilder.CreateIndex(
                name: "ix_orders_user_id_created_at",
                table: "orders",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_processed_notices_order_id",
                table: "processed_notices",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_queue_entries_event_id_order_key_sequence",
                table: "queue_entries",
                columns: new[] { "event_id", "order_key", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_queue_entries_user_id",
                table: "queue_entries",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "processed_notices");

            migrationBuilder.DropTable(
                name: "queue_entries");

            migrationBuilder.DropTable(
                name: "holds");

            migrationBuilder.DropSequence(
                name: "queue_sequence");
        }
    }
}
