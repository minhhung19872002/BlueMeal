using System.Reflection;

namespace BlueMeal.Application.Authorization;

/// <summary>
/// Single source of truth for permission codes (<c>module.action</c>). Synced to table <c>permission</c> at startup
/// (ASSUMPTIONS A-05). Any public const string inside a nested class is a permission.
/// </summary>
public static class Permissions
{
    /// <summary>Implicit permission granted to every authenticated user (own profile, notifications…).</summary>
    public const string Self = "self";

    public static class Organization { public const string View = "organization.view"; public const string Edit = "organization.edit"; }
    public static class Department { public const string View = "department.view"; public const string Create = "department.create"; public const string Edit = "department.edit"; public const string Delete = "department.delete"; public const string Import = "department.import"; public const string Export = "department.export"; }
    public static class CommonCatalog { public const string View = "common_catalog.view"; public const string Create = "common_catalog.create"; public const string Edit = "common_catalog.edit"; public const string Delete = "common_catalog.delete"; public const string Import = "common_catalog.import"; public const string Export = "common_catalog.export"; }
    public static class Employee { public const string View = "employee.view"; public const string Create = "employee.create"; public const string Edit = "employee.edit"; public const string Delete = "employee.delete"; public const string Import = "employee.import"; public const string Export = "employee.export"; }
    public static class Pii { public const string View = "pii.view"; public const string Manage = "pii.manage"; }
    public static class Role { public const string View = "role.view"; public const string Create = "role.create"; public const string Edit = "role.edit"; public const string Delete = "role.delete"; }
    public static class User { public const string View = "user.view"; public const string Create = "user.create"; public const string Edit = "user.edit"; public const string Delete = "user.delete"; public const string Lock = "user.lock"; public const string ResetPassword = "user.reset_password"; }
    public static class Session { public const string View = "session.view"; public const string Revoke = "session.revoke"; }
    public static class Permission { public const string View = "permission.view"; }
    public static class SystemInfo { public const string Version = "system.version"; public const string Setting = "system.setting"; public const string SettingEdit = "system.setting_edit"; public const string Backup = "system.backup"; public const string Restore = "system.restore"; public const string SampleData = "system.sample_data"; }
    public static class Audit { public const string View = "audit.view"; public const string Export = "audit.export"; }
    public static class Integration { public const string View = "integration.view"; public const string Edit = "integration.edit"; public const string Sync = "integration.sync"; public const string Resolve = "integration.resolve"; }

    public static class Subject { public const string View = "subject.view"; public const string Create = "subject.create"; public const string Edit = "subject.edit"; public const string Delete = "subject.delete"; public const string Import = "subject.import"; public const string Export = "subject.export"; }
    public static class Partner { public const string View = "partner.view"; public const string Create = "partner.create"; public const string Edit = "partner.edit"; public const string Delete = "partner.delete"; public const string Import = "partner.import"; public const string Export = "partner.export"; }
    public static class Customer { public const string View = "customer.view"; public const string Create = "customer.create"; public const string Edit = "customer.edit"; public const string Delete = "customer.delete"; public const string Import = "customer.import"; public const string Export = "customer.export"; }

    public static class Warehouse { public const string View = "warehouse.view"; public const string Create = "warehouse.create"; public const string Edit = "warehouse.edit"; public const string Delete = "warehouse.delete"; public const string Import = "warehouse.import"; public const string Export = "warehouse.export"; }
    public static class FoodGroup { public const string View = "food_group.view"; public const string Create = "food_group.create"; public const string Edit = "food_group.edit"; public const string Delete = "food_group.delete"; public const string Import = "food_group.import"; public const string Export = "food_group.export"; }
    public static class Nutrient { public const string View = "nutrient.view"; public const string Create = "nutrient.create"; public const string Edit = "nutrient.edit"; public const string Delete = "nutrient.delete"; public const string Import = "nutrient.import"; public const string Export = "nutrient.export"; }
    public static class Food { public const string View = "food.view"; public const string Create = "food.create"; public const string Edit = "food.edit"; public const string Delete = "food.delete"; public const string Import = "food.import"; public const string Export = "food.export"; public const string Unlock = "food.unlock"; }
    public static class FoodSafety { public const string View = "food_safety.view"; public const string Create = "food_safety.create"; public const string Edit = "food_safety.edit"; public const string Delete = "food_safety.delete"; }
    public static class Ingredient { public const string View = "ingredient.view"; public const string Create = "ingredient.create"; public const string Edit = "ingredient.edit"; public const string Delete = "ingredient.delete"; public const string Import = "ingredient.import"; public const string Export = "ingredient.export"; public const string Price = "ingredient.price"; }
    public static class Dish { public const string View = "dish.view"; public const string Create = "dish.create"; public const string Edit = "dish.edit"; public const string Delete = "dish.delete"; public const string Import = "dish.import"; public const string Export = "dish.export"; }
    public static class Recipe { public const string View = "recipe.view"; public const string Create = "recipe.create"; public const string Edit = "recipe.edit"; public const string Delete = "recipe.delete"; public const string Approve = "recipe.approve"; public const string Export = "recipe.export"; }
    public static class Recommendation { public const string View = "recommendation.view"; public const string Create = "recommendation.create"; public const string Edit = "recommendation.edit"; public const string Delete = "recommendation.delete"; public const string Import = "recommendation.import"; public const string Export = "recommendation.export"; }
    public static class ReportGroup { public const string View = "report_group.view"; public const string Create = "report_group.create"; public const string Edit = "report_group.edit"; public const string Delete = "report_group.delete"; }
    public static class MealShift { public const string View = "meal_shift.view"; public const string Create = "meal_shift.create"; public const string Edit = "meal_shift.edit"; public const string Delete = "meal_shift.delete"; }
    public static class MealStructure { public const string View = "meal_structure.view"; public const string Create = "meal_structure.create"; public const string Edit = "meal_structure.edit"; public const string Delete = "meal_structure.delete"; }

    public static class MenuPlan { public const string View = "menu_plan.view"; public const string Create = "menu_plan.create"; public const string Edit = "menu_plan.edit"; public const string Delete = "menu_plan.delete"; }
    public static class Menu
    {
        public const string View = "menu.view"; public const string Create = "menu.create"; public const string Edit = "menu.edit"; public const string Delete = "menu.delete";
        public const string Submit = "menu.submit"; public const string Approve = "menu.approve"; public const string ApproveFinal = "menu.approve_final";
        public const string Publish = "menu.publish"; public const string Cancel = "menu.cancel"; public const string Recall = "menu.recall";
        public const string OverrideCritical = "menu.override_critical"; public const string Export = "menu.export"; public const string ViewCost = "menu.view_cost";
    }
    public static class MealOrder { public const string View = "meal_order.view"; public const string Create = "meal_order.create"; public const string Edit = "meal_order.edit"; public const string Delete = "meal_order.delete"; }
    public static class Nutrition { public const string View = "nutrition.view"; public const string Export = "nutrition.export"; }
    public static class Alert { public const string View = "alert.view"; public const string Resolve = "alert.resolve"; public const string Config = "alert.config"; }
    public static class Cost { public const string View = "cost.view"; public const string Config = "cost.config"; public const string Export = "cost.export"; }

    public static class StockReceipt { public const string View = "stock.receipt.view"; public const string Create = "stock.receipt.create"; public const string Edit = "stock.receipt.edit"; public const string Delete = "stock.receipt.delete"; public const string Post = "stock.receipt.post"; }
    public static class StockIssue { public const string View = "stock.issue.view"; public const string Create = "stock.issue.create"; public const string Edit = "stock.issue.edit"; public const string Delete = "stock.issue.delete"; public const string Post = "stock.issue.post"; }
    public static class StockTake { public const string View = "stock.take.view"; public const string Create = "stock.take.create"; public const string Edit = "stock.take.edit"; public const string Approve = "stock.take.approve"; }
    public static class Stock { public const string View = "stock.view"; public const string Lock = "stock.lock"; }
    public static class PurchaseRequest { public const string View = "purchase.view"; public const string Create = "purchase.create"; public const string Edit = "purchase.edit"; public const string Approve = "purchase.approve"; }
    public static class Inspection { public const string View = "inspection.view"; public const string Create = "inspection.create"; public const string Edit = "inspection.edit"; public const string Delete = "inspection.delete"; }
    public static class Sample { public const string View = "sample.view"; public const string Create = "sample.create"; public const string Edit = "sample.edit"; public const string Dispose = "sample.dispose"; }

    public static class Report { public const string View = "report.view"; public const string Export = "report.export"; }
    public static class Dashboard { public const string View = "dashboard.view"; }

    // ---------------------------------------------------------------------------------------------

    public sealed record PermissionInfo(string Code, string Module, string Action, string NameVi, string NameEn, int SortOrder);

    private static readonly Dictionary<string, (string Vi, string En)> ModuleNames = new()
    {
        ["organization"] = ("Thông tin đơn vị", "Organization"), ["department"] = ("Cơ cấu tổ chức", "Departments"),
        ["common_catalog"] = ("Danh mục chung", "Common catalogs"), ["employee"] = ("Nhân viên", "Employees"),
        ["pii"] = ("Dữ liệu cá nhân", "Personal data"), ["role"] = ("Nhóm quyền", "Roles"), ["user"] = ("Tài khoản người dùng", "User accounts"),
        ["session"] = ("Phiên làm việc", "Sessions"), ["permission"] = ("Danh sách chức năng", "Functions"),
        ["system"] = ("Hệ thống", "System"), ["audit"] = ("Nhật ký hệ thống", "Audit log"), ["integration"] = ("Tích hợp", "Integration"),
        ["subject"] = ("Đối tượng", "Subject groups"), ["partner"] = ("Đối tác", "Partners"), ["customer"] = ("Khách hàng", "Customers"),
        ["warehouse"] = ("Kho", "Warehouses"), ["food_group"] = ("Nhóm thực phẩm", "Food groups"), ["nutrient"] = ("Chất dinh dưỡng", "Nutrients"),
        ["food"] = ("Thực phẩm", "Foods"), ["food_safety"] = ("ATTP, chất lượng thực phẩm", "Food safety"), ["ingredient"] = ("Nguyên liệu", "Ingredients"),
        ["dish"] = ("Món ăn", "Dishes"), ["recipe"] = ("Công thức", "Recipes"), ["recommendation"] = ("Khuyến nghị", "Recommendations"),
        ["report_group"] = ("Nhóm báo cáo", "Report groups"), ["meal_shift"] = ("Ca ăn", "Meal shifts"), ["meal_structure"] = ("Cơ cấu bữa ăn", "Meal structures"),
        ["menu_plan"] = ("Kế hoạch thực đơn", "Menu plans"), ["menu"] = ("Thực đơn", "Menus"), ["meal_order"] = ("Đặt suất ăn", "Meal orders"),
        ["nutrition"] = ("Phân tích dinh dưỡng", "Nutrition analysis"), ["alert"] = ("Cảnh báo", "Alerts"), ["cost"] = ("Chi phí suất ăn", "Meal cost"),
        ["stock.receipt"] = ("Phiếu nhập kho", "Stock receipts"), ["stock.issue"] = ("Phiếu xuất kho", "Stock issues"), ["stock.take"] = ("Kiểm kê kho", "Stock takes"),
        ["stock"] = ("Tồn kho", "Inventory"), ["purchase"] = ("Đề xuất mua hàng", "Purchase requests"), ["inspection"] = ("Kiểm thực 3 bước", "3-step inspection"),
        ["sample"] = ("Lưu mẫu thức ăn", "Food samples"), ["report"] = ("Báo cáo", "Reports"), ["dashboard"] = ("Bảng điều khiển", "Dashboard"),
    };

    private static readonly Dictionary<string, (string Vi, string En)> ActionNames = new()
    {
        ["view"] = ("Xem", "View"), ["create"] = ("Thêm", "Create"), ["edit"] = ("Sửa", "Edit"), ["delete"] = ("Xóa", "Delete"),
        ["import"] = ("Nhập Excel", "Import"), ["export"] = ("Xuất Excel", "Export"), ["approve"] = ("Duyệt", "Approve"),
        ["approve_final"] = ("Duyệt cấp cuối", "Final approval"), ["submit"] = ("Gửi duyệt", "Submit"), ["publish"] = ("Công bố", "Publish"),
        ["cancel"] = ("Hủy", "Cancel"), ["recall"] = ("Thu hồi", "Recall"), ["override_critical"] = ("Chấp nhận cảnh báo nghiêm trọng", "Override critical warnings"),
        ["view_cost"] = ("Xem chi phí", "View cost"), ["lock"] = ("Khóa/mở", "Lock/unlock"), ["reset_password"] = ("Đặt lại mật khẩu", "Reset password"),
        ["revoke"] = ("Đăng xuất cưỡng bức", "Force logout"), ["version"] = ("Phiên bản hệ thống", "System version"),
        ["setting"] = ("Xem cấu hình", "View settings"), ["setting_edit"] = ("Sửa cấu hình", "Edit settings"), ["backup"] = ("Sao lưu", "Backup"),
        ["restore"] = ("Khôi phục", "Restore"), ["sample_data"] = ("Quản lý dữ liệu mẫu", "Manage sample data"), ["sync"] = ("Đồng bộ", "Synchronize"),
        ["resolve"] = ("Xử lý", "Resolve"), ["unlock"] = ("Mở khóa dữ liệu API", "Unlock API data"), ["price"] = ("Quản lý giá", "Manage prices"),
        ["config"] = ("Cấu hình", "Configure"), ["post"] = ("Ghi sổ", "Post"), ["dispose"] = ("Hủy mẫu", "Dispose"), ["manage"] = ("Xuất/ẩn danh hóa", "Export/anonymize"),
    };

    private static readonly Lazy<IReadOnlyList<PermissionInfo>> _all = new(() =>
    {
        var list = new List<PermissionInfo>();
        var order = 0;
        foreach (var type in typeof(Permissions).GetNestedTypes(BindingFlags.Public | BindingFlags.Static))
        {
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string)))
            {
                var code = (string)f.GetRawConstantValue()!;
                var idx = code.LastIndexOf('.');
                var module = code[..idx];
                var action = code[(idx + 1)..];
                var m = ModuleName(module);
                var a = ActionName(action);
                list.Add(new PermissionInfo(code, module, action, $"{m.Vi} – {a.Vi}", $"{m.En} – {a.En}", ++order));
            }
        }
        return list;
    });

    public static IReadOnlyList<PermissionInfo> All => _all.Value;

    public static (string Vi, string En) ModuleName(string module) => ModuleNames.TryGetValue(module, out var n) ? n : (Vi: module, En: module);
    public static (string Vi, string En) ActionName(string action) => ActionNames.TryGetValue(action, out var n) ? n : (Vi: action, En: action);
}
