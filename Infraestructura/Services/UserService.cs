﻿// Weather.infra/Services/UserService.cs
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Core.DTO;
using Core.Entities;
using Infraestructura.Repositories;

namespace Infraestructura.Services
{
    public class UserService
    {
        private readonly UserRepository _repo;
        private readonly IConfiguration  _config;

        public UserService(UserRepository repo, IConfiguration config)
        {
            _repo   = repo;
            _config = config;
        }

        public async Task<AnswerAuthenticationDTO> RegisterAsync(CredentialsUserDTO creds)
        {
            var user = new Usuario { UserName = creds.Email, Email = creds.Email };
            var result = await _repo.CreateUserAsync(user, creds.Password!);
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

            return await BuildTokenAsync(creds);
        }

        public async Task<AnswerAuthenticationDTO> LoginAsync(CredentialsUserDTO creds)
        {
            var result = await _repo.PasswordSignInAsync(creds.Email, creds.Password!);
            if (!result.Succeeded)
                throw new UnauthorizedAccessException();

            return await BuildTokenAsync(creds);
        }

        public IEnumerable<UserDTO> GetAllUsers()
        {
            return _repo.GetAllUsers()
                .Select(u => new UserDTO { Id = u.Id, Email = u.Email! })
                .ToList();
        }

        public async Task ChangeOwnPasswordAsync(string userId, ChangePasswordDTO dto)
        {
            var user = await _repo.FindByIdAsync(userId)
                       ?? throw new KeyNotFoundException("Usuario no encontrado.");

            var result = await _repo.ChangePasswordAsync(
                user,
                dto.CurrentPassword,
                dto.NewPassword);

            if (!result.Succeeded)
            {
                // recojo todos los errores en un mensaje único
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException(errors);
            }
        }

        public async Task DeleteUserAsync(string userId)
        {
            var user = await _repo.FindByIdAsync(userId)
                       ?? throw new KeyNotFoundException();

            var result = await _repo.DeleteUserAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        public async Task AssignAdminAsync(string email)
        {
            var user = await _repo.FindByEmailAsync(email) 
                       ?? throw new KeyNotFoundException();

            var claims = await _repo.GetClaimsAsync(user);
            if (claims.Any(c => c.Type == "isAdmin" && c.Value == "true"))
                throw new InvalidOperationException();

            var result = await _repo.AddClaimAsync(user, new Claim("isAdmin", "true"));
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        private async Task<AnswerAuthenticationDTO> BuildTokenAsync(CredentialsUserDTO creds)
        {
            // 1) Busca al usuario en BD
            var user = await _repo.FindByEmailAsync(creds.Email)
                       ?? throw new KeyNotFoundException("User not found.");

            // 2) Empieza la lista de claims incluyendo el NameIdentifier
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id!),
                new Claim(ClaimTypes.Email, creds.Email),
                new Claim("securityStamp", user.SecurityStamp!)
            };

            // 3) Añade los claims extra que haya en la BD
            var dbClaims = await _repo.GetClaimsAsync(user);
            claims.AddRange(dbClaims);

            // 4) Añade rol de admin si aplica
            var roles = await _repo.GetRolesAsync(user);
            if (roles.Contains("Admin"))
            {
                claims.Add(new Claim("isAdmin", "true"));
            }

            // 5) Recupera configuración de JWT
            var jwtSection = _config.GetSection("Jwt");
            var keyBytes = Encoding.UTF8.GetBytes(jwtSection["Key"]!);
            var signingKey = new SymmetricSecurityKey(keyBytes);
            var credsSign = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            // 6) Genera el token
            var expires = DateTime.UtcNow.AddYears(1);
            var token = new JwtSecurityToken(
                issuer: jwtSection["Issuer"],
                audience: jwtSection["Audience"],
                claims: claims,
                expires: expires,
                signingCredentials: credsSign
            );

            // 7) Devuelve DTO con string del token y fecha de expiración
            return new AnswerAuthenticationDTO
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                Expiration = expires
            };
        }

    }
}
