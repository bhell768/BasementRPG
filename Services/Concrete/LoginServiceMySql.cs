using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using BasementDnD.Configuration;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.Logging;
using BasementDnD.Services.Abstract;
using BasementDnD.Models.Login;
using BasementDnD.Models;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BasementDnD.Services.Concrete
{
    public class LoginServiceMySql : MySqlRepository, ILoginService
    {    
        private readonly IHttpContextAccessor _httpContextAccessor;
        public LoginServiceMySql(PersistentSettings persistentSettings, IHttpContextAccessor httpContextAccessor) : base(persistentSettings)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<bool> Login(LoginRequest request)
        {
            Login user;
            using (var conn = Connection)
            {
                await conn.OpenAsync();
                var cmd = conn.CreateCommand() as MySqlCommand;
                cmd.CommandText = @"SELECT `Id_Bin`, `Name`,`Username`, `Email`, `Password` FROM `users` WHERE `Username` = @username";
                BindUName(cmd, request.Username);
                var result = await ReadAllAsync(await cmd.ExecuteReaderAsync());
                user = result.Count > 0 ? result[0] : null;
            }
            //will replace with pbkdf2 sha512 for hashing
            if(user == null || user.Password != request.Password)
            {
                    return false;
            }
            
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("ID", Convert.ToBase64String(user.Id_Bin)),
                new Claim(ClaimTypes.Role, "Tester"), //add in roles and full name
                new Claim("FullName", user.Displayname),
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            var authProperties = new AuthenticationProperties
            {
                AllowRefresh = true,

                IsPersistent = request.Persistent,


            };

            _httpContextAccessor.HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme, 
                new ClaimsPrincipal(claimsIdentity), 
                authProperties);

            return true;
        }

        public async Task<bool> Logout()
        {
            //not sure what can go wrong here
            //will implement any exception handeling
            //not sure what happens if a logout is attempted on a not logged in user
            _httpContextAccessor.HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);
            return true;
        }

        public async Task<LoginInfoResponse> GetInfo()
        {
            LoginInfoResponse info = new LoginInfoResponse();
            byte[] isLogged = VerifyLogin();
            if(isLogged == null)
            {
                info.Islogged = false;
                info.Username = "";
                info.Displayname = "";
            }
            else
            {
                info.Islogged = true;
                info.Username = _httpContextAccessor.HttpContext.User.Identity.Name;
                info.Displayname = _httpContextAccessor.HttpContext.User.FindFirst("FullName").Value;
            }
            return info;
        }

        public async Task<bool> SignUp(SignupRequest request)
        {
            using (var conn = Connection)
            {
                await conn.OpenAsync();
                var cmd = conn.CreateCommand() as MySqlCommand;
                cmd.CommandText = @"INSERT INTO `users` (`Id_Bin`, `Name`,`Username`, `Email`,`Password`) VALUES (unhex(replace(uuid(),'-','')), @name, @username, @email, @password);";
                BindParams(cmd, request);
                await cmd.ExecuteNonQueryAsync();
                //cmd.LastInsertedId returns long of the autogenerated id
                //does not work with uuid
            }

            //just create request and call the login that will handle finding the uuid
            LoginRequest logRequest = new LoginRequest();
            logRequest.Username = request.Username;
            logRequest.Password = request.Password;
            logRequest.Persistent = request.Persistent;

            return await Login(logRequest);
        }

        public byte[] VerifyLogin()
        {
            if(!_httpContextAccessor.HttpContext.User.Identity.IsAuthenticated)
            {
                return null;
            }
            return Convert.FromBase64String(_httpContextAccessor.HttpContext.User.FindFirst("ID").Value);
        }

        private void BindId(MySqlCommand cmd, byte[] id)
        {
            cmd.Parameters.Add(new MySqlParameter
            {
                ParameterName = "@id",
                DbType = DbType.Binary,
                Value = id,
            });
        }

        private void BindUName(MySqlCommand cmd, string username)
        {
            cmd.Parameters.Add(new MySqlParameter
            {
                ParameterName = "@username",
                DbType = DbType.String,
                Value = username,
            });
        }
        
        private void BindParams(MySqlCommand cmd, SignupRequest request)
        {
            cmd.Parameters.Add(new MySqlParameter
            {
                ParameterName = "@username",
                DbType = DbType.String,
                Value = request.Username,
            });
            cmd.Parameters.Add(new MySqlParameter
            {
                ParameterName = "@email",
                DbType = DbType.String,
                Value = request.Email,
            });
            cmd.Parameters.Add(new MySqlParameter
            {
                ParameterName = "@name",
                DbType = DbType.String,
                Value = request.Displayname,
            });
            cmd.Parameters.Add(new MySqlParameter
            {
                ParameterName = "@password",
                DbType = DbType.String,
                Value = request.Password,
            });
        }
        
        private async Task<List<Login>> ReadAllAsync(DbDataReader reader)
        {
            var logins = new List<Login>();
            using (reader)
            {
                while (await reader.ReadAsync())
                {
                    var login = new Login()
                    {
                        Id_Bin = await reader.GetFieldValueAsync<byte[]>(0),
                        Displayname = await reader.GetFieldValueAsync<string>(1),
                        Username = await reader.GetFieldValueAsync<string>(2),
                        Email = await reader.GetFieldValueAsync<string>(3),
                        Password = await reader.GetFieldValueAsync<string>(4)
                    };
                    logins.Add(login);
                }
            }
            return logins;
        }
    }
}