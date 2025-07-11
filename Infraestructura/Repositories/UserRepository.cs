﻿// Weather.infra/Repositories/UserRepository.cs
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Core.Entities;

namespace Infraestructura.Repositories
{
    public class UserRepository
    {
        private readonly UserManager<Usuario> _userManager;
        private readonly SignInManager<Usuario> _signInManager;

        public UserRepository(UserManager<Usuario> userManager,SignInManager<Usuario> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        public Task<IdentityResult> CreateUserAsync(Usuario user, string password) =>
            _userManager.CreateAsync(user, password);

        public Task<SignInResult> PasswordSignInAsync(string email, string password) =>
            _signInManager.PasswordSignInAsync(email, password, false, false);

        public Task<Usuario?> FindByIdAsync(string id) =>
            _userManager.FindByIdAsync(id);

        public Task<Usuario?> FindByEmailAsync(string email) =>
            _userManager.FindByEmailAsync(email);

        public Task<IdentityResult> ChangePasswordAsync(Usuario user,string currentPassword,string newPassword)
        {
            return _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        }

        public Task<IdentityResult> DeleteUserAsync(Usuario user) =>
            _userManager.DeleteAsync(user);

        public IQueryable<Usuario> GetAllUsers() =>
            _userManager.Users;

        public Task<IList<Claim>> GetClaimsAsync(Usuario user) =>
            _userManager.GetClaimsAsync(user);

        public Task<IList<string>> GetRolesAsync(Usuario user) =>
            _userManager.GetRolesAsync(user);

        public Task<IdentityResult> AddClaimAsync(Usuario user, Claim claim) =>
            _userManager.AddClaimAsync(user, claim);
    }
}
