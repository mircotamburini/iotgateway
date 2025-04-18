using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WalkingTec.Mvvm.Core.Extensions;
using WalkingTec.Mvvm.Core.Models;
using WalkingTec.Mvvm.Core.Support.Json;

namespace WalkingTec.Mvvm.Core
{
    /// <summary>
    /// FrameworkContext
    /// </summary>
    public partial class FrameworkContext : EmptyContext, IDataContext
    {
        public DbSet<FrameworkMenu> BaseFrameworkMenus { get; set; }
        public DbSet<FunctionPrivilege> BaseFunctionPrivileges { get; set; }
        public DbSet<DataPrivilege> BaseDataPrivileges { get; set; }
        public DbSet<FileAttachment> BaseFileAttachments { get; set; }
        public DbSet<FrameworkGroup> BaseFrameworkGroups { get; set; }
        public DbSet<FrameworkRole> BaseFrameworkRoles { get; set; }
        public DbSet<FrameworkUserRole> BaseFrameworkUserRoles { get; set; }
        public DbSet<FrameworkUserGroup> BaseFrameworkUserGroups { get; set; }
        public DbSet<FrameworkWorkflow> FrameworkWorkflows { get; set; }
        public DbSet<ActionLog> BaseActionLogs { get; set; }
        public DbSet<FrameworkTenant> FrameworkTenants { get; set; }
        public DbSet<Elsa_Bookmark> Elsa_Bookmarks { get; set; }
        public DbSet<Elsa_Trigger> Elsa_Triggers { get; set; }
        public DbSet<Elsa_WorkflowDefinition> Elsa_WorkflowDefinitions { get; set; }
        public DbSet<Elsa_WorkflowExecutionLogRecord> Elsa_WorkflowExecutionLogRecords { get; set; }
        public DbSet<Elsa_WorkflowInstance> Elsa_WorkflowInstances { get; set; }

        /// <summary>
        /// FrameworkContext
        /// </summary>
        public FrameworkContext() : base()
        {
        }

        /// <summary>
        /// FrameworkContext
        /// </summary>
        /// <param name="cs"></param>
        public FrameworkContext(string cs) : base(cs)
        {
        }

        public FrameworkContext(string cs, DBTypeEnum dbtype, string version = null) : base(cs, dbtype, version)
        {
        }

        public FrameworkContext(CS cs) : base(cs)
        {
        }

        public FrameworkContext(DbContextOptions options) : base(options)
        {
        }

        /// <summary>
        /// OnModelCreating
        /// </summary>
        /// <param name="modelBuilder"></param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            //菜单和菜单权限的级联删除
            //wtmtodo: 删除菜单同步删除页面权限
            //modelBuilder.Entity<FunctionPrivilege>().HasOne(x => x.MenuItem).WithMany(x => x.Privileges).HasForeignKey(x => x.MenuItemId).OnDelete(DeleteBehavior.Cascade);

            var allTypes = Utils.GetAllModels();

            foreach (var item in allTypes)
            {
                if (typeof(TopBasePoco).IsAssignableFrom(item) && typeof(ISubFile).IsAssignableFrom(item) == false)
                {
                    //将所有关联附件的外键设为不可级联删除
                    var pros = item.GetProperties().Where(x => x.PropertyType == typeof(FileAttachment)).ToList();
                    foreach (var filepro in pros)
                    {
                        var builder = typeof(ModelBuilder).GetMethod("Entity", Type.EmptyTypes).MakeGenericMethod(item).Invoke(modelBuilder, null) as EntityTypeBuilder;
                        builder.HasOne(filepro.Name).WithMany().OnDelete(DeleteBehavior.Restrict);
                    }
                }
                List<Expression> list = new List<Expression>();
                ParameterExpression pe = Expression.Parameter(item);
                if (item.BaseType == null || item.BaseType.IsAbstract == true || (item.BaseType == typeof(TopBasePoco) || item.BaseType == typeof(BasePoco) || item.BaseType?.BaseType == typeof(TreePoco)))
                {
                    if (typeof(IPersistPoco).IsAssignableFrom(item))
                    {
                        var exp = Expression.Equal(Expression.Property(pe, "IsValid"), Expression.Constant(true));
                        list.Add(exp);
                    }
                    if (typeof(ITenant).IsAssignableFrom(item))
                    {
                        var exp = Expression.Equal(Expression.Property(pe, "TenantCode"), Expression.PropertyOrField(Expression.Constant(this), "TenantCode"));
                        list.Add(exp);
                    }
                    if (list.Count > 0)
                    {
                        var finalexp = list[0];
                        for (int i = 1; i < list.Count; i++)
                        {
                            finalexp = Expression.AndAlso(finalexp, list[i]);
                        }
                        var builder = typeof(ModelBuilder).GetMethod("Entity", Type.EmptyTypes).MakeGenericMethod(item).Invoke(modelBuilder, null) as EntityTypeBuilder;
                        builder.HasQueryFilter(Expression.Lambda(finalexp, pe));
                    }
                }
            }
        }

        /// <summary>
        /// 数据初始化
        /// </summary>
        /// <param name="allModules"></param>
        /// <param name="IsSpa"></param>
        /// <returns>返回true表示需要进行初始化数据操作，返回false即数据库已经存在或不需要初始化数据</returns>
        public override async Task<bool> DataInit(object allModules, bool IsSpa)
        {
            bool rv = await Database.EnsureCreatedAsync();
            //判断是否存在初始数据
            bool emptydb = false;
            try
            {
                emptydb = Set<FrameworkUserRole>().Count() == 0 && Set<FrameworkMenu>().Count() == 0;
            }
            catch { }

            if (emptydb == true)
            {
                var AllModules = allModules as List<SimpleModule>;
                var roles = new FrameworkRole[]
                {
                    new FrameworkRole{ ID = Guid.NewGuid(), RoleCode = "001", RoleName = CoreProgram._localizer?["Sys.Admin"], TenantCode=TenantCode},
                    new FrameworkRole{ ID = Guid.NewGuid(), RoleCode = "002", RoleName = CoreProgram._localizer?["_Admin.User"], TenantCode=TenantCode},
                };

                var adminRole = roles[0];
                if (Set<FrameworkMenu>().Any() == false && TenantCode == null)
                {
                    var systemManagement = GetFolderMenu("SystemManagement");
                    var logList = IsSpa ? GetMenu2(AllModules, "ActionLog", "MenuKey.ActionLog", 1) : GetMenu(AllModules, "_Admin", "ActionLog", "Index", "MenuKey.ActionLog", 1);
                    var userList = IsSpa ? GetMenu2(AllModules, "FrameworkUser", "MenuKey.UserManagement", 2) : GetMenu(AllModules, "_Admin", "FrameworkUser", "Index", "MenuKey.UserManagement", 2);
                    var roleList = IsSpa ? GetMenu2(AllModules, "FrameworkRole", "MenuKey.RoleManagement", 3) : GetMenu(AllModules, "_Admin", "FrameworkRole", "Index", "MenuKey.RoleManagement", 3);
                    var groupList = IsSpa ? GetMenu2(AllModules, "FrameworkGroup", "MenuKey.GroupManagement", 4) : GetMenu(AllModules, "_Admin", "FrameworkGroup", "Index", "MenuKey.GroupManagement", 4);
                    var menuList = IsSpa ? GetMenu2(AllModules, "FrameworkMenu", "MenuKey.MenuMangement", 5) : GetMenu(AllModules, "_Admin", "FrameworkMenu", "Index", "MenuKey.MenuMangement", 5);
                    var dpList = IsSpa ? GetMenu2(AllModules, "DataPrivilege", "MenuKey.DataPrivilege", 6) : GetMenu(AllModules, "_Admin", "DataPrivilege", "Index", "MenuKey.DataPrivilege", 6);
                    var tenantList = IsSpa ? GetMenu2(AllModules, "FrameworkTenant", "MenuKey.FrameworkTenant", 6) : GetMenu(AllModules, "_Admin", "FrameworkTenant", "Index", "MenuKey.FrameworkTenant", 7);
                    if (logList != null)
                    {
                        var menus = new FrameworkMenu[] { logList, userList, roleList, groupList, menuList, dpList, tenantList };
                        foreach (var item in menus)
                        {
                            if (item != null)
                            {
                                systemManagement.Children.Add(item);
                            }
                        }
                        Set<FrameworkMenu>().Add(systemManagement);
                        Set<FunctionPrivilege>().AddRange(systemManagement.FlatTree().Select(x => new FunctionPrivilege { RoleCode = "001", MenuItemId = x.ID, Allowed = true }));

                        if (IsSpa == false)
                        {
                            systemManagement.Icon = "layui-icon layui-icon-set";
                            logList?.SetPropertyValue("Icon", "layui-icon layui-icon-form");
                            userList?.SetPropertyValue("Icon", "layui-icon layui-icon-friends");
                            roleList?.SetPropertyValue("Icon", "layui-icon layui-icon-user");
                            groupList?.SetPropertyValue("Icon", "layui-icon layui-icon-group");
                            menuList?.SetPropertyValue("Icon", "layui-icon layui-icon-menu-fill");
                            dpList?.SetPropertyValue("Icon", "layui-icon layui-icon-auz");
                            tenantList?.SetPropertyValue("Icon", " layui-icon layui-icon-share");

                            var apifolder = GetFolderMenu("Api");
                            apifolder.ShowOnMenu = false;
                            apifolder.DisplayOrder = 100;
                            var logList2 = GetMenu2(AllModules, "ActionLog", "MenuKey.ActionLog", 1);
                            var userList2 = GetMenu2(AllModules, "FrameworkUser", "MenuKey.UserManagement", 2);
                            var roleList2 = GetMenu2(AllModules, "FrameworkRole", "MenuKey.RoleManagement", 3);
                            var groupList2 = GetMenu2(AllModules, "FrameworkGroup", "MenuKey.GroupManagement", 4);
                            var menuList2 = GetMenu2(AllModules, "FrameworkMenu", "MenuKey.MenuMangement", 5);
                            var dpList2 = GetMenu2(AllModules, "DataPrivilege", "MenuKey.DataPrivilege", 6);
                            var tenantList2 = GetMenu2(AllModules, "FrameworkTenant", "MenuKey.FrameworkTenant", 7);
                            var apis = new FrameworkMenu[] { logList2, userList2, roleList2, groupList2, menuList2, dpList2, tenantList2 };
                            //apis.ToList().ForEach(x => { x.ShowOnMenu = false;x.PageName += $"({Program._localizer["BuildinApi"]})"; });
                            foreach (var item in apis)
                            {
                                if (item != null)
                                {
                                    item.ModuleName += "Api";
                                    item.ShowOnMenu = false;
                                    apifolder.Children.Add(item);
                                }
                            }
                            Set<FrameworkMenu>().Add(apifolder);
                            Set<FunctionPrivilege>().AddRange(apifolder.FlatTree().Select(x => new FunctionPrivilege { RoleCode = "001", MenuItemId = x.ID, Allowed = true }));
                        }
                        else
                        {
                            systemManagement.Icon = " _wtmicon _wtmicon-icon_shezhi";
                            logList?.SetPropertyValue("Icon", " _wtmicon _wtmicon-chaxun");
                            userList?.SetPropertyValue("Icon", " _wtmicon _wtmicon-zhanghaoquanxianguanli");
                            roleList?.SetPropertyValue("Icon", " _wtmicon _wtmicon-quanxianshenpi");
                            groupList?.SetPropertyValue("Icon", " _wtmicon _wtmicon-zuzhiqunzu");
                            menuList?.SetPropertyValue("Icon", " _wtmicon _wtmicon--lumingpai");
                            dpList?.SetPropertyValue("Icon", " _wtmicon _wtmicon-anquan");
                            tenantList?.SetPropertyValue("Icon", " _wtmicon _wtmicon-fenxiangfangshi");
                        }
                    }
                }
                Set<FrameworkRole>().AddRange(roles);
                await SaveChangesAsync();
            }
            return rv;
        }

        private FrameworkMenu GetFolderMenu(string FolderText, bool isShowOnMenu = true, bool isInherite = false)
        {
            FrameworkMenu menu = new FrameworkMenu
            {
                PageName = "MenuKey." + FolderText,
                Children = new List<FrameworkMenu>(),
                ShowOnMenu = isShowOnMenu,
                IsInside = true,
                FolderOnly = true,
                IsPublic = false,
                TenantAllowed = true,
                DisplayOrder = 1
            };
            return menu;
        }

        private FrameworkMenu GetMenu(List<SimpleModule> allModules, string areaName, string controllerName, string actionName, string pageKey, int displayOrder)
        {
            var acts = allModules.Where(x => x.ClassName == controllerName && (areaName == null || x.Area?.Prefix?.ToLower() == areaName.ToLower())).SelectMany(x => x.Actions).ToList();
            var act = acts.Where(x => x.MethodName == actionName).SingleOrDefault();
            var rest = acts.Where(x => x.MethodName != actionName && x.IgnorePrivillege == false).ToList();
            bool allowtenant = controllerName != "FrameworkMenu";
            FrameworkMenu menu = GetMenuFromAction(act, true, displayOrder, allowtenant);
            if (act?.Module?.IsApi == true)
            {
                menu.ModuleName += "Api";
            }
            if (menu != null)
            {
                menu.PageName = pageKey;
                for (int i = 0; i < rest.Count; i++)
                {
                    if (rest[i] != null)
                    {
                        var sub = GetMenuFromAction(rest[i], false, (i + 1), allowtenant);
                        sub.PageName = pageKey;
                        if (rest[i].Module.IsApi == true)
                        {
                            sub.ModuleName += "Api";
                        }
                        menu.Children.Add(sub);
                    }
                }
            }
            return menu;
        }

        private FrameworkMenu GetMenu2(List<SimpleModule> allModules, string controllerName, string pageKey, int displayOrder)
        {
            bool allowtenant = controllerName != "FrameworkMenu";
            var acts = allModules.Where(x => (x.FullName == $"WalkingTec.Mvvm.Admin.Api,{controllerName}") && x.IsApi == true).SelectMany(x => x.Actions).ToList();
            var rest = acts.Where(x => x.IgnorePrivillege == false).ToList();
            SimpleAction act = null;
            if (acts.Count > 0)
            {
                act = acts[0];
            }
            FrameworkMenu menu = GetMenuFromAction(act, true, displayOrder, allowtenant);
            if (menu != null)
            {
                menu.PageName = pageKey;
                menu.Url = "/" + acts[0].Module.ClassName.ToLower();
                menu.ActionName = "MainPage";
                menu.ClassName = acts[0].Module.FullName;
                menu.MethodName = null;
                for (int i = 0; i < rest.Count; i++)
                {
                    if (rest[i] != null)
                    {
                        var sub = GetMenuFromAction(rest[i], false, (i + 1), allowtenant);
                        sub.PageName = pageKey;
                        menu.Children.Add(sub);
                    }
                }
            }
            return menu;
        }

        private FrameworkMenu GetMenuFromAction(SimpleAction act, bool isMainLink, int displayOrder = 1, bool allowtenant = true)
        {
            if (act == null)
            {
                return null;
            }
            FrameworkMenu menu = new FrameworkMenu
            {
                //ActionId = act.ID,
                //ModuleId = act.ModuleId,
                ClassName = act.Module.FullName,
                MethodName = act.MethodName,
                Url = act.Url,
                ShowOnMenu = isMainLink,
                FolderOnly = false,
                Children = new List<FrameworkMenu>(),
                IsPublic = false,
                IsInside = true,
                DisplayOrder = displayOrder,
                TenantAllowed = allowtenant
            };
            if (isMainLink)
            {
                menu.ModuleName = act.Module.ModuleName;
                menu.ActionName = act.ActionDes?.Description ?? act.ActionName;
                menu.MethodName = null;
            }
            else
            {
                menu.ModuleName = act.Module.ModuleName;
                menu.ActionName = act.ActionDes?.Description ?? act.ActionName;
            }
            return menu;
        }
    }

    public partial class EmptyContext : DbContext, IDataContext
    {
        private ILoggerFactory _loggerFactory;

        /// <summary>
        /// Commited
        /// </summary>
        public bool Commited { get; set; }

        /// <summary>
        /// IsFake
        /// </summary>
        public bool IsFake { get; set; }

        private string _tenantCode;

        public string TenantCode
        {
            get
            {
                return _tenantCode;
            }
        }

        public bool IsDebug { get; set; }

        /// <summary>
        /// CSName
        /// </summary>
        public string CSName { get; set; }

        public DBTypeEnum DBType { get; set; }

        public string Version { get; set; }
        public CS ConnectionString { get; set; }

        /// <summary>
        /// FrameworkContext
        /// </summary>
        public EmptyContext()
        {
            CSName = "default";
            DBType = DBTypeEnum.SqlServer;
        }

        /// <summary>
        /// FrameworkContext
        /// </summary>
        /// <param name="cs"></param>
        public EmptyContext(string cs)
        {
            CSName = cs;
            DBType = DBTypeEnum.SqlServer;
        }

        public EmptyContext(string cs, DBTypeEnum dbtype, string version = null)
        {
            CSName = cs;
            DBType = dbtype;
            Version = version;
        }

        public EmptyContext(CS cs)
        {
            CSName = cs.Value;
            DBType = cs.DbType ?? DBTypeEnum.SqlServer;
            Version = cs.Version;
            ConnectionString = cs;
        }

        public EmptyContext(DbContextOptions options) : base(options)
        {
        }

        public IDataContext CreateNew()
        {
            if (ConnectionString != null)
            {
                return (IDataContext)this.GetType().GetConstructor(new Type[] { typeof(CS) }).Invoke(new object[] { ConnectionString }); ;
            }
            else
            {
                return (IDataContext)this.GetType().GetConstructor(new Type[] { typeof(string), typeof(DBTypeEnum), typeof(string) }).Invoke(new object[] { CSName, DBType, Version });
            }
        }

        public IDataContext ReCreate(ILoggerFactory _logger = null)
        {
            if (this?.Database?.CurrentTransaction != null)
            {
                return this;
            }
            else
            {
                IDataContext rv = null;
                if (ConnectionString != null)
                {
                    rv = (IDataContext)this.GetType().GetConstructor(new Type[] { typeof(CS) }).Invoke(new object[] { ConnectionString }); ;
                }
                else
                {
                    rv = (IDataContext)this.GetType().GetConstructor(new Type[] { typeof(string), typeof(DBTypeEnum) }).Invoke(new object[] { CSName, DBType });
                }
                rv.SetTenantCode(this.TenantCode);
                if (_logger != null)
                {
                    rv.IsDebug = true;
                    rv.SetLoggerFactory(_logger);
                }
                return rv;
            }
        }

        /// <summary>
        /// 将一个实体设为填加状态
        /// </summary>
        /// <param name="entity">实体</param>
        public void AddEntity<T>(T entity) where T : TopBasePoco
        {
            this.Entry(entity).State = EntityState.Added;
        }

        /// <summary>
        /// 将一个实体设为修改状态
        /// </summary>
        /// <param name="entity">实体</param>
        public void UpdateEntity<T>(T entity) where T : TopBasePoco
        {
            this.Entry(entity).State = EntityState.Modified;
        }

        /// <summary>
        /// 将一个实体的某个字段设为修改状态，用于只更新个别字段的情况
        /// </summary>
        /// <typeparam name="T">实体类</typeparam>
        /// <param name="entity">实体</param>
        /// <param name="fieldExp">要设定为修改状态的字段</param>
        public void UpdateProperty<T>(T entity, Expression<Func<T, object>> fieldExp)
            where T : TopBasePoco
        {
            var set = this.Set<T>();
            if (set.Local.AsQueryable().CheckID(entity.GetID()).FirstOrDefault() == null)
            {
                set.Attach(entity);
            }
            this.Entry(entity).Property(fieldExp).IsModified = true;
        }

        /// <summary>
        /// UpdateProperty
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        /// <param name="fieldName"></param>
        public void UpdateProperty<T>(T entity, string fieldName)
            where T : TopBasePoco
        {
            var set = this.Set<T>();
            if (set.Local.AsQueryable().CheckID(entity.GetID()).FirstOrDefault() == null)
            {
                set.Attach(entity);
            }
            this.Entry(entity).Property(fieldName).IsModified = true;
        }

        /// <summary>
        /// 将一个实体设定为删除状态
        /// </summary>
        /// <param name="entity">实体</param>
        public void DeleteEntity<T>(T entity) where T : TopBasePoco
        {
            var set = this.Set<T>();
            var exist = set.Local.AsQueryable().CheckID(entity.GetID()).FirstOrDefault();
            if (exist == null)
            {
                set.Attach(entity);
                set.Remove(entity);
            }
            else
            {
                set.Remove(exist);
            }
        }

        /// <summary>
        /// CascadeDelete
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        public void CascadeDelete<T>(T entity) where T : TreePoco
        {
            if (entity != null && entity.ID != Guid.Empty)
            {
                var set = this.Set<T>();
                var entities = set.Where(x => x.ParentId == entity.ID).ToList();
                if (entities.Count > 0)
                {
                    foreach (var item in entities)
                    {
                        CascadeDelete(item);
                    }
                }
                DeleteEntity(entity);
            }
        }

        /// <summary>
        /// GetCoreType
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public Type GetCoreType(Type t)
        {
            if (t != null && t.IsNullable())
            {
                if (!t.GetTypeInfo().IsValueType)
                {
                    return t;
                }
                else
                {
                    if ("DateTime".Equals(t.GenericTypeArguments[0].Name))
                    {
                        return typeof(string);
                    }
                    return Nullable.GetUnderlyingType(t);
                }
            }
            else
            {
                if ("DateTime".Equals(t.Name))
                {
                    return typeof(string);
                }
                return t;
            }
        }

        /// <summary>
        /// OnModelCreating
        /// </summary>
        /// <param name="modelBuilder"></param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (DBType == DBTypeEnum.Oracle)
            {
                modelBuilder.Model.SetMaxIdentifierLength(30);
                modelBuilder.Entity<Elsa_Bookmark>().ToTable("Bookmarks");
                modelBuilder.Entity<Elsa_Trigger>().ToTable("Triggers");
                modelBuilder.Entity<Elsa_WorkflowDefinition>().ToTable("WorkflowDefinitions");
                modelBuilder.Entity<Elsa_WorkflowExecutionLogRecord>().ToTable("WorkflowExecutionLogRecords");
                modelBuilder.Entity<Elsa_WorkflowInstance>().ToTable("WorkflowInstances");
            }
        }

        /// <summary>
        /// OnConfiguring
        /// </summary>
        /// <param name="optionsBuilder"></param>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            switch (DBType)
            {
                case DBTypeEnum.SqlServer:
                    var ver = 120;
                    if (string.IsNullOrEmpty(Version) == false)
                    {
                        int.TryParse(Version, out ver);
                    }
                    optionsBuilder.UseSqlServer(CSName, o => o.UseCompatibilityLevel(ver));
                    break;

                case DBTypeEnum.MySql:
                    ServerVersion sv = null;
                    if (string.IsNullOrEmpty(Version) == false)
                    {
                        ServerVersion.TryParse(Version, out sv);
                    }
                    if (sv == null)
                    {
                        sv = ServerVersion.AutoDetect(CSName);
                    }
                    optionsBuilder.UseMySql(CSName, sv, b => b.SchemaBehavior(MySqlSchemaBehavior.Translate,
    (schema, entity) => $"{entity}"));
                    break;

                case DBTypeEnum.PgSql:
                    optionsBuilder.UseNpgsql(CSName);
                    break;

                case DBTypeEnum.Memory:
                    optionsBuilder.UseInMemoryDatabase(CSName);
                    break;

                case DBTypeEnum.SQLite:
                    optionsBuilder.UseSqlite(CSName);
                    break;
                //case DBTypeEnum.DaMeng:
                //    optionsBuilder.UseDm(CSName);
                //    break;
                case DBTypeEnum.Oracle:
                    optionsBuilder.UseOracle(CSName, option =>
                    {
                        switch (Version)
                        {
                            case "19":
                                option.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion19);
                                break;

                            case "21":
                                option.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion21);
                                break;

                            case "23":
                                option.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion23);
                                break;

                            default:
                                option.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion19);
                                break;
                        }
                    });
                    break;

                default:
                    break;
            }
            if (IsDebug == true)
            {
                optionsBuilder.EnableDetailedErrors();
                optionsBuilder.EnableSensitiveDataLogging();
                if (_loggerFactory != null)
                {
                    optionsBuilder.UseLoggerFactory(_loggerFactory);
                }
            }
            base.OnConfiguring(optionsBuilder);
        }

        public void SetLoggerFactory(ILoggerFactory factory)
        {
            this._loggerFactory = factory;
        }

        public void SetTenantCode(string code)
        {
            this._tenantCode = code;
        }

        /// <summary>
        /// 数据初始化
        /// </summary>
        /// <param name="allModules"></param>
        /// <param name="IsSpa"></param>
        /// <returns>返回true表示需要进行初始化数据操作，返回false即数据库已经存在或不需要初始化数据</returns>
        public virtual async Task<bool> DataInit(object allModules, bool IsSpa)
        {
            bool rv = await Database.EnsureCreatedAsync();
            return rv;
        }

        #region 执行存储过程返回datatable

        /// <summary>
        /// 执行存储过程，返回datatable结果集
        /// </summary>
        /// <param name="command">存储过程名称</param>
        /// <param name="paras">存储过程参数</param>
        /// <returns></returns>
        public DataTable RunSP(string command, params object[] paras)
        {
            return Run(command, CommandType.StoredProcedure, paras);
        }

        #endregion 执行存储过程返回datatable

        public IEnumerable<TElement> RunSP<TElement>(string command, params object[] paras)
        {
            return Run<TElement>(command, CommandType.StoredProcedure, paras);
        }

        #region 执行Sql语句，返回datatable

        public DataTable RunSQL(string sql, params object[] paras)
        {
            return Run(sql, CommandType.Text, paras);
        }

        #endregion 执行Sql语句，返回datatable

        public IEnumerable<TElement> RunSQL<TElement>(string sql, params object[] paras)
        {
            return Run<TElement>(sql, CommandType.Text, paras);
        }

        #region 执行存储过程或Sql语句返回DataTable

        /// <summary>
        /// 执行存储过程或Sql语句返回DataTable
        /// </summary>
        /// <param name="sql">存储过程名称或Sql语句</param>
        /// <param name="commandType">命令类型</param>
        /// <param name="paras">参数</param>
        /// <returns></returns>
        public DataTable Run(string sql, CommandType commandType, params object[] paras)
        {
            DataTable table = new DataTable();
            var connection = this.Database.GetDbConnection();
            var isClosed = connection.State == ConnectionState.Closed;
            if (isClosed)
            {
                connection.Open();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.CommandTimeout = 2400;
                command.CommandType = commandType;
                if (this.Database.CurrentTransaction != null)
                {
                    command.Transaction = this.Database.CurrentTransaction.GetDbTransaction();
                }
                if (paras != null)
                {
                    foreach (var param in paras)
                        command.Parameters.Add(param);
                }
                using (var reader = command.ExecuteReader())
                {
                    table.Load(reader);
                }
            }
            if (isClosed)
            {
                connection.Close();
            }
            return table;
        }

        #endregion 执行存储过程或Sql语句返回DataTable

        public IEnumerable<TElement> Run<TElement>(string sql, CommandType commandType, params object[] paras)
        {
            IEnumerable<TElement> entityList = new List<TElement>();
            DataTable dt = Run(sql, commandType, paras);
            entityList = EntityHelper.GetEntityList<TElement>(dt);
            return entityList;
        }

        public object CreateCommandParameter(string name, object value, ParameterDirection dir)
        {
            object rv = null;
            switch (this.DBType)
            {
                case DBTypeEnum.SqlServer:
                    rv = new SqlParameter(name, value) { Direction = dir };
                    break;

                case DBTypeEnum.MySql:
                    rv = new MySqlParameter(name, value) { Direction = dir };
                    break;

                case DBTypeEnum.PgSql:
                    rv = new NpgsqlParameter(name, value) { Direction = dir };
                    break;

                case DBTypeEnum.SQLite:
                    rv = new SqliteParameter(name, value) { Direction = dir };
                    break;

                case DBTypeEnum.Oracle:
                    //rv = new OracleParameter(name, value) { Direction = dir };
                    break;
            }
            return rv;
        }

        public void EnsureCreate()
        {
            this.Database.EnsureCreated();
        }
    }

    public class NullContext : IDataContext
    {
        public string TenantCode { get; }
        public bool IsFake { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public IModel Model => throw new NotImplementedException();

        public DatabaseFacade Database => throw new NotImplementedException();

        public string CSName { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public DBTypeEnum DBType { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public bool IsDebug { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void AddEntity<T>(T entity) where T : TopBasePoco
        {
            throw new NotImplementedException();
        }

        public void CascadeDelete<T>(T entity) where T : TreePoco
        {
            throw new NotImplementedException();
        }

        public object CreateCommandParameter(string name, object value, ParameterDirection dir)
        {
            throw new NotImplementedException();
        }

        public IDataContext CreateNew()
        {
            throw new NotImplementedException();
        }

        public Task<bool> DataInit(object AllModel, bool IsSpa)
        {
            throw new NotImplementedException();
        }

        public void DeleteEntity<T>(T entity) where T : TopBasePoco
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }

        public IDataContext ReCreate(ILoggerFactory _logger = null)
        {
            throw new NotImplementedException();
        }

        public DataTable Run(string sql, CommandType commandType, params object[] paras)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<TElement> Run<TElement>(string sql, CommandType commandType, params object[] paras)
        {
            throw new NotImplementedException();
        }

        public DataTable RunSP(string command, params object[] paras)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<TElement> RunSP<TElement>(string command, params object[] paras)
        {
            throw new NotImplementedException();
        }

        public DataTable RunSQL(string command, params object[] paras)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<TElement> RunSQL<TElement>(string sql, params object[] paras)
        {
            throw new NotImplementedException();
        }

        public int SaveChanges()
        {
            throw new NotImplementedException();
        }

        public int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            throw new NotImplementedException();
        }

        public Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public DbSet<T> Set<T>() where T : class
        {
            throw new NotImplementedException();
        }

        public void SetTenantCode(string code)
        {
            throw new NotImplementedException();
        }

        public void SetLoggerFactory(ILoggerFactory factory)
        {
            throw new NotImplementedException();
        }

        public void UpdateEntity<T>(T entity) where T : TopBasePoco
        {
            throw new NotImplementedException();
        }

        public void UpdateProperty<T>(T entity, Expression<Func<T, object>> fieldExp) where T : TopBasePoco
        {
            throw new NotImplementedException();
        }

        public void UpdateProperty<T>(T entity, string fieldName) where T : TopBasePoco
        {
            throw new NotImplementedException();
        }

        public void EnsureCreate()
        {
            throw new NotImplementedException();
        }
    }
}