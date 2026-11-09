namespace BlueMeal.Application.Authorization;

/// <summary>Seeded roles (SPEC §4). Admin can edit permissions afterwards; seed only fills roles that do not exist yet.</summary>
public static class DefaultRoles
{
    public const string SysAdmin = "SYS_ADMIN";
    public const string Manager = "MANAGER";
    public const string MenuApprover = "MENU_APPROVER";
    public const string Nutritionist = "NUTRITIONIST";
    public const string HeadChef = "HEAD_CHEF";
    public const string Storekeeper = "STOREKEEPER";
    public const string Accountant = "ACCOUNTANT";
    public const string CustomerRep = "CUSTOMER_REP";

    /// <summary>Roles for which MFA is mandatory (SPEC §10.1).</summary>
    public static readonly string[] MfaMandatory = [SysAdmin, Manager, MenuApprover, Accountant];

    public sealed record RoleSeed(string Code, string NameVi, string NameEn, string Description, Func<string, bool> Grants);

    /// <summary>True when code starts with any of the given module prefixes (each ends with '.').</summary>
    private static bool Any(string code, params string[] prefixes) => prefixes.Any(p => code.StartsWith(p, StringComparison.Ordinal));
    private static bool IsView(string code) => code.EndsWith(".view", StringComparison.Ordinal) || code == Permissions.SystemInfo.Version;

    public static readonly RoleSeed[] All =
    [
        new(SysAdmin, "Quản trị hệ thống", "System administrator", "Toàn quyền quản trị", _ => true),
        new(Manager, "Lãnh đạo / Quản lý", "Manager", "Xem toàn bộ dashboard, báo cáo; duyệt thực đơn cấp cuối",
            c => IsView(c) || Any(c, "report.", "dashboard.", "nutrition.", "cost.") || c is Permissions.Menu.ApproveFinal or Permissions.Menu.Approve
                 or Permissions.Menu.ViewCost or Permissions.Menu.Export or Permissions.Menu.Publish or Permissions.Menu.OverrideCritical),
        new(MenuApprover, "Người duyệt thực đơn", "Menu approver", "Duyệt/từ chối thực đơn; xem dinh dưỡng và chi phí",
            c => c is Permissions.Menu.View or Permissions.Menu.Approve or Permissions.Menu.ViewCost or Permissions.Menu.Publish or Permissions.Menu.Export
                 or Permissions.Menu.OverrideCritical or Permissions.MenuPlan.View or Permissions.Nutrition.View or Permissions.Nutrition.Export
                 or Permissions.Cost.View or Permissions.Alert.View or Permissions.Dish.View or Permissions.Recipe.View or Permissions.Dashboard.View
                 or Permissions.Report.View or Permissions.Report.Export or Permissions.SystemInfo.Version or Permissions.Recommendation.View
                 or Permissions.Subject.View or Permissions.Customer.View or Permissions.MealShift.View or Permissions.MealStructure.View),
        new(Nutritionist, "Cán bộ dinh dưỡng", "Nutritionist", "Quản lý thực phẩm, khuyến nghị, món, công thức; lập thực đơn",
            c => Any(c, "food.", "food_group.", "nutrient.", "recommendation.", "dish.", "recipe.", "menu_plan.", "nutrition.", "alert.", "meal_structure.", "meal_shift.", "subject.", "food_safety.")
                 || c is Permissions.Menu.View or Permissions.Menu.Create or Permissions.Menu.Edit or Permissions.Menu.Delete or Permissions.Menu.Submit
                 or Permissions.Menu.Export or Permissions.Menu.ViewCost or Permissions.Ingredient.View or Permissions.Ingredient.Create or Permissions.Ingredient.Edit
                 or Permissions.Integration.View or Permissions.Integration.Sync or Permissions.Integration.Resolve or Permissions.Dashboard.View
                 or Permissions.Report.View or Permissions.Report.Export or Permissions.SystemInfo.Version or Permissions.Customer.View or Permissions.MealOrder.View
                 or Permissions.Cost.View or Permissions.Stock.View),
        new(HeadChef, "Bếp trưởng", "Head chef", "Lập thực đơn, công thức, kiểm thực 3 bước, lưu mẫu, đề xuất xuất kho",
            c => Any(c, "recipe.", "inspection.", "sample.") || c is Permissions.Dish.View or Permissions.Dish.Create or Permissions.Dish.Edit
                 or Permissions.Menu.View or Permissions.Menu.Create or Permissions.Menu.Edit or Permissions.Menu.Submit or Permissions.MenuPlan.View or Permissions.MenuPlan.Create or Permissions.MenuPlan.Edit
                 or Permissions.StockIssue.View or Permissions.StockIssue.Create or Permissions.PurchaseRequest.View or Permissions.PurchaseRequest.Create
                 or Permissions.Ingredient.View or Permissions.Food.View or Permissions.Nutrition.View or Permissions.Alert.View or Permissions.Dashboard.View
                 or Permissions.Report.View or Permissions.Report.Export or Permissions.SystemInfo.Version or Permissions.FoodSafety.View or Permissions.MealShift.View
                 or Permissions.MealStructure.View or Permissions.Subject.View or Permissions.Stock.View or Permissions.MealOrder.View),
        new(Storekeeper, "Thủ kho", "Storekeeper", "Nhập, xuất, kiểm kê kho; kiểm tra chất lượng đầu vào",
            c => Any(c, "stock.receipt.", "stock.issue.", "stock.take.") || c is Permissions.Stock.View or Permissions.PurchaseRequest.View or Permissions.PurchaseRequest.Create or Permissions.PurchaseRequest.Edit
                 or Permissions.Ingredient.View or Permissions.Warehouse.View or Permissions.Partner.View or Permissions.Inspection.View or Permissions.Inspection.Create or Permissions.Inspection.Edit
                 or Permissions.FoodSafety.View or Permissions.Alert.View or Permissions.Dashboard.View or Permissions.Report.View or Permissions.Report.Export
                 or Permissions.SystemInfo.Version or Permissions.Menu.View),
        new(Accountant, "Kế toán", "Accountant", "Giá nguyên liệu, chi phí suất ăn, báo cáo nhập-xuất-tồn, báo cáo chi phí",
            c => Any(c, "cost.", "report.") || c is Permissions.Ingredient.View or Permissions.Ingredient.Price or Permissions.Ingredient.Export or Permissions.Stock.View or Permissions.Stock.Lock
                 or Permissions.StockReceipt.View or Permissions.StockIssue.View or Permissions.StockTake.View or Permissions.Partner.View or Permissions.Customer.View
                 or Permissions.Menu.View or Permissions.Menu.ViewCost or Permissions.MealOrder.View or Permissions.Dashboard.View or Permissions.SystemInfo.Version
                 or Permissions.PurchaseRequest.View or Permissions.PurchaseRequest.Approve),
        new(CustomerRep, "Đại diện đơn vị đặt suất ăn", "Customer representative", "Xem thực đơn đã công bố, đặt và điều chỉnh số suất",
            c => c is Permissions.Menu.View or Permissions.MealOrder.View or Permissions.MealOrder.Create or Permissions.MealOrder.Edit or Permissions.SystemInfo.Version
                 or Permissions.Dashboard.View),
    ];
}
