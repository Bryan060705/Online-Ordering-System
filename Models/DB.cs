using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Demo.Models;

#nullable disable warnings

public class DB : DbContext
{
    public DB(DbContextOptions options) : base(options) { }
    

    // DbSet
    public DbSet<Admin> Admins { get; set; }
    public DbSet<Member> Members{ get; set; }
    public DbSet<Table> Tables { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<ProductImage> ProductImages { get; set; }
    public DbSet<Ad> Ads { get; set; }
    public DbSet<QrLoginToken> QrLoginTokens { get; set; }
    public DbSet<Voucher> Vouchers { get; set; }
    public DbSet<MemberVoucher> MemberVouchers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>()
            .HasOne(o => o.Member)
            .WithMany()
            .HasForeignKey(o => o.MemberId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

// Entity Classes -------------------------------------------------------------

public class Admin
{
    public int Id { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    // 用於表單輸入但不存 DB
    [NotMapped]
    public string? Password { get; set; }

    // 真正存到 DB 的 PasswordHash
    public string PasswordHash { get; set; } = string.Empty;

    public string? Email { get; set; } // SuperAdmin 才需要

    public bool IsSuperAdmin { get; set; } = false;

    public string? PhotoPath { get; set; }
}

public class Member
{
    public int Id { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    // 用於表單輸入但不存 DB
    [NotMapped]
    public string? Password { get; set; }

    // 真正存到 DB 的 PasswordHash
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    public string Email { get; set; } = string.Empty;

    public int Point { get; set; }

    public string? PhotoPath { get; set; }

    // Email 驗證
    public bool IsEmailConfirmed { get; set; } = false;

    public string? EmailConfirmToken { get; set; }
    public DateTime? EmailConfirmTokenExpiry { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiry { get; set; }
}

public class Table
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public int Capacity { get; set; }

    public bool IsAvailable { get; set; } = true;

    // 一个 Table 可以有多个订单
    public List<Order>? Orders { get; set; }
}

public class Product
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    // 一个产品有多张图片
    public List<ProductImage>? Images { get; set; }
    // 售卖状态
    public bool IsActive { get; set; } = true;  // 默认开启
}

public class ProductImage
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Required]
    public string ImagePath { get; set; } = string.Empty;
}

public class OrderItem
{
    public int Id { get; set; }

    // 外键：Order
    public int OrderId { get; set; }
    public Order? Order { get; set; }

    // 外键：Product
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Range(1, 100)]
    public int Quantity { get; set; }

    // 下单时的单价（避免以后改价影响旧订单）
    [Precision(18, 2)]
    public decimal UnitPrice { get; set; }

    // ✅ 小计（单价 * 数量）
    [Precision(18, 2)]
    public decimal TotalPrice { get; set; }

    public bool IsVoucher { get; set; } = false;
    public int? MemberVoucherId { get; set; }
    public MemberVoucher? MemberVoucher { get; set; }

    [NotMapped]
    public string DisplayName
    {
        get
        {
            if (IsVoucher && MemberVoucher != null && MemberVoucher.Voucher != null)
            {
                return $"{MemberVoucher.Voucher.Name} x {Product?.Name}";
            }
            return Product?.Name ?? "Unknown Product";
        }
    }
}

public class Order
{
    public int Id { get; set; }

    // 会员用户
    public int? MemberId { get; set; }

    // 访客用户
    public string? GuestId { get; set; }

    // 餐桌（仅限堂食）
    public int? TableId { get; set; }

    // 外带 / 堂食
    public bool IsTakeAway { get; set; }

    // 下单时间
    [DataType(DataType.DateTime)]
    public DateTime OrderDate { get; set; }

    // 是否付款
    public bool IsPaid { get; set; }

    // 真实导航属性（存进数据库的订单项）
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    // 如果后台需要「手动创建订单」，就保留；否则可以删掉
    [NotMapped]
    public List<OrderItem> OrderItemsTemp { get; set; } = new();

    // 订单总价（不会存数据库，每次取值时计算）
    [NotMapped]
    public decimal TotalPrice => OrderItems?.Sum(i => i.UnitPrice * i.Quantity) ?? 0;

    // 导航属性
    public Member? Member { get; set; }
    public Table? Table { get; set; }
}

public class CartItem
{
    public int ProductId { get; set; }
    public string? ProductName { get; set; }

    // ✅ 改名成 UnitPrice，和 OrderItem 对齐
    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }
    public string? ImagePath { get; set; }

    // ✅ 自动计算总价
    public decimal Total => UnitPrice * Quantity;

    // Voucher相关属性
    public bool IsVoucherApplied { get; set; }
    public int? MemberVoucherId { get; set; }
    public string? VoucherName { get; set; }
}

public class Ad
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public string? Url { get; set; }

    public string? ImagePath { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; } = 0;
}

public class QrLoginToken
{
    public int Id { get; set; }
    public int MemberId { get; set; }

    public Member? Member { get; set; }

    // 隨機 token（不含明文密碼）
    public string Token { get; set; } = string.Empty;

    public DateTime Expiry { get; set; }

    // 一次性使用可設 true，預設 false 允許多次掃描
    public bool IsUsed { get; set; } = false;
}


public class Voucher
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Detail { get; set; }

    public int ValidDay { get; set; }  // 有效天数

    public int PointNeeded { get; set; } // 需要的积分

    public int Limit { get; set; } // 总领取次数

    public int? ProductId { get; set; } // 关联的产品ID
    public Product? Product { get; set; }

    [Precision(18, 2)]
    public decimal DiscountedPrice { get; set; } // 折扣后的价格

    // 已经领取的次数（用于判断是否领完）
    public int RedeemedCount { get; set; } = 0;
}

public class MemberVoucher
{
    public int Id { get; set; }

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    public int VoucherId { get; set; }
    public Voucher? Voucher { get; set; }

    public DateTime RedeemedDate { get; set; } // 领取时间

    public DateTime ExpiryDate { get; set; } // 过期时间

    public bool IsUsed { get; set; } = false; // 是否已使用
}

