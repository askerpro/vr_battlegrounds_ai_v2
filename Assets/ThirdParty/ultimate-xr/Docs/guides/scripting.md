---
title: "Scripting"
---

# Scripting

UltimateXR was designed from the beginning to be an easy-to-use yet powerful and scalable framework. Regarding the code, we always tried to apply the highest standards.

These are some of the principles that we tried to follow:

- Contribute considering that any small improvement will help cutting hours of work to many other coworkers and developers. 
- Write well-structured and clean code. Framework code will be used in many projects, during many years by many developers. Write the API that we would love existed.
- Documentation is a top priority:
  -  Every class, every method, every event, or property is fully documented using standard XML tags. No matter the visibility, public, protected, or private. This will enable programmers to visualize contextual help in the IDE whenever they access any UltimateXR class or member. It also provides a way to automatically create the API documentation on this website.
  -  In longer sections of code, inserting comments every few steps indicating what happens next compartmentalizes the algorithm. At the same time, it helps getting a quicker idea of what each part does even if it could be inferred by looking at the code itself.
- Always write code when it is required, not because it could be useful. Every single line of code in the framework was written because it was required in a project. By doing this, the API will naturally evolve to how it makes sense in real use cases.
  If we want to implement new features that are not required, we need to create a real-world scenario where it would be used. 
- Follow standards whenever possible. We tried to follow the .NET Framework Design Guidelines: https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/
- Use a common file structure and code style to keep homogeneity across the whole framework. We currently use Rider and its code style capabilities and have used Resharper for Visual Studio in the past. We may provide these configuration files in the future to facilitate external contributions.

Here are some tips to help you navigate through the code:

## Collapsed view

Use Ctrl+M followed by Ctrl+O to collapse to definitions. Our source files are grouped in sections that follow a logic order, and the collapsed view helps getting a quick look at what is available and how the file is organized. Public properties, events and methods will be shown on top and will help you understand how the components are used.

![](/guides/media/scripting/01Collapsed.png)

## Expanded view

Use Ctrl+M followed by Ctrl+L to expand the definitions. The collapse and expand shortcuts will help you alternate between the two views to quickly navigate through the code. 

![](/guides/media/scripting/02Expanded.png)

## Code completion

Make use of the autocompletion and tooltips in your IDE, such as Intellisense in Visual Studio or the built-in capabilities in Rider. The framework has full XML documentation which among other things will assist you when you write code:

![](/guides/media/scripting/03AutoCompletion.png)
 
To check commonly used code snippets that will help you developing your VR apps navigate to the [How Do I...](/guides/scripting-how-do-i) section. It will also help you understand which classes are involved in the different aspects of development.

If you need help regarding specific classes it is recommended to visit the [API](/api) section where all functionality is detailed.
