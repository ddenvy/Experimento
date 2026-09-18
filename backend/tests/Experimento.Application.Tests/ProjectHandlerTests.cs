using Experimento.Application.Abstractions;
using Experimento.Application.Exceptions;
using Experimento.Application.Features.Projects;
using Experimento.Domain.Entities;
using NSubstitute;

namespace Experimento.Application.Tests;

/// <summary>
/// Tests for project use-case handlers (mocked DbContext).
/// </summary>
public class ProjectHandlerTests
{
    [Fact]
    public async Task CreateProject_AddsProjectAndReturnsDto()
    {
        var projects = TestDbSet.Create<Project>();
        var db = Substitute.For<IAppDbContext>();
        db.Projects.Returns(projects);

        var createdBy = Guid.NewGuid();
        var handler = new CreateProjectHandler(db);

        var result = await handler.Handle(new CreateProjectCommand("Test Project", "Desc", createdBy), CancellationToken.None);

        Assert.Equal("Test Project", result.Name);
        Assert.Equal("Desc", result.Description);
        projects.Received(1).Add(Arg.Is<Project>(p => p.Name == "Test Project" && p.CreatedBy == createdBy));
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateProject_DuplicateName_ThrowsConflict()
    {
        var createdBy = Guid.NewGuid();
        var projects = TestDbSet.Create(new[]
        {
            new Project { Name = "Test Project", CreatedBy = createdBy }
        });
        var db = Substitute.For<IAppDbContext>();
        db.Projects.Returns(projects);

        var handler = new CreateProjectHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new CreateProjectCommand("Test Project", null, createdBy), CancellationToken.None));
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateProject_SameNameForOtherUser_IsAllowed()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var projects = TestDbSet.Create(new[]
        {
            new Project { Name = "Test Project", CreatedBy = owner }
        });
        var db = Substitute.For<IAppDbContext>();
        db.Projects.Returns(projects);

        var handler = new CreateProjectHandler(db);

        var result = await handler.Handle(new CreateProjectCommand("Test Project", null, other), CancellationToken.None);
        Assert.Equal("Test Project", result.Name);
    }

    [Fact]
    public async Task UpdateProject_ThrowsWhenNotFound()
    {
        var db = Substitute.For<IAppDbContext>();
        db.Projects.FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns((Project?)null);

        var handler = new UpdateProjectHandler(db, new ResourceAuthorization(db));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new UpdateProjectCommand(Guid.NewGuid(), "X", null), CancellationToken.None));
    }
}
